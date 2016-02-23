/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;

namespace Opc.Ua.Server
{       
#if LEGACY_CORENODEMANAGER
    /// <summary>
    /// Defines additional methods for a StateMachine
    /// </summary>
    public partial class StateMachine
    {          
        #region Initialization
        /// <summary>
        /// Creates the members uses to store the state machine structure.
        /// </summary>
        protected override void OnAfterCreate(object configuration)
        {
            base.OnAfterCreate(configuration);                        
            ConstructMachine(configuration);
        }
        
        /// <summary>
        /// Contructs the machine by reading the address space.
        /// </summary>
        protected void ConstructMachine(object configuration)
        {
            m_causes = new Dictionary<QualifiedName,MethodSource>();
            m_substateMachines = new List<StateMachine>();

            foreach (ILocalNode source in NodeManager.GetLocalNodes(this.NodeId, ReferenceTypeIds.HasComponent, false, true))
            {
                StateMachine substate = source as StateMachine;

                if (substate != null)
                {
                    // substate machines always report events to their parent.
                    substate.ReportEventsToParent = true;
                    m_substateMachines.Add(substate);
                    continue;
                }

                MethodSource cause = source as MethodSource;

                if (cause != null)
                {
                    m_causes.Add(cause.BrowseName, cause);
                    continue;
                }
            }

            ConstructMachineFromType(this.TypeDefinitionId);

            // get the parent state.
            ILocalNode parentState = NodeManager.GetTargetNode(this.NodeId, ReferenceTypes.HasSubStateMachine, false, true, null);

            if (parentState != null)
            {
                m_parentState = parentState.BrowseName;
            }

            GotoInitialState();
        }
        #endregion
        
        #region Public Interface
        /// <summary>
        /// Contructs the machine by reading the address space.
        /// </summary>
        protected void ConstructMachineFromType(NodeId typeDefinitionId)
        {
            lock (DataLock)
            {
                m_initialStateName = null;
                m_states = new Dictionary<QualifiedName,State>();
                m_transitions = new List<Transition>();

                // find all components.
                foreach (ILocalNode source in NodeManager.GetInstanceDeclarations(typeDefinitionId, null))
                {
                    if (Server.TypeTree.IsTypeOf(source.TypeDefinitionId, ObjectTypes.StateType))
                    {
                        State state = new State(source);

                        m_states.Add(state.BrowseName, state);
                        
                        if (Server.TypeTree.IsTypeOf(source.TypeDefinitionId, ObjectTypes.InitialStateType))
                        {
                            m_initialStateName = state;
                        }

                        IVariable stateNumber = NodeManager.GetTargetNode(
                            source.NodeId, 
                            ReferenceTypes.HasProperty, 
                            false,
                            true, 
                            BrowseNames.StateNumber) as IVariable;
                        
                        if (stateNumber != null && typeof(uint).IsInstanceOfType(stateNumber.Value))
                        {
                            state.StateNumber = (uint)stateNumber.Value;
                        }

                        continue;
                    }
                    
                    if (Server.TypeTree.IsTypeOf(source.TypeDefinitionId, ObjectTypes.TransitionType))
                    {
                        AddTransition(source);
                        continue;
                    }
                }

                // set the initial state if none provided.
                if (m_initialStateName == null)
                {
                    uint stateNumber = UInt32.MaxValue;

                    foreach (State state in m_states.Values)
                    {
                        if (state.StateNumber < stateNumber)
                        {
                            stateNumber = state.StateNumber;
                            m_initialStateName = state;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Called to process a transition that was found in the machine.
        /// </summary>
        protected virtual void AddTransition(ILocalNode source)
        {
            Transition transition = new Transition(source);

            // save the transition.
            m_transitions.Add(transition);
            
            // add the from state.
            ILocalNode fromState = NodeManager.GetTargetNode(source.NodeId, ReferenceTypes.FromState, false, true, null);
            
            if (fromState != null)
            {
                transition.FromState = m_states[fromState.BrowseName];
            }

            // add the to state.
            ILocalNode toState = NodeManager.GetTargetNode(source.NodeId, ReferenceTypes.ToState, false, true, null);
            
            if (fromState != null)
            {
                transition.ToState = m_states[toState.BrowseName];
            }

            // find all causes.
            foreach (ILocalNode cause in NodeManager.GetLocalNodes(source.NodeId, ReferenceTypes.HasCause, false, true))
            {
                MethodSource method = null;
 
                if (m_causes.TryGetValue(cause.BrowseName, out method))
                {
                    transition.Causes.Add(method);
                }
            }

            // find all effects.
            foreach (ILocalNode effect in NodeManager.GetLocalNodes(source.NodeId, ReferenceTypes.HasEffect, false, true))
            {
                transition.Effects.Add(effect.NodeId);
            }
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// The browse name of the current state.
        /// </summary>
        public QualifiedName CurrentStateBrowseName
        {
            get
            {
                lock (DataLock)
                {
                    if (!m_currentState.NameSpecified)
                    {
                        return QualifiedName.Null;
                    }

                    return m_currentState.Name.Value;
                }
            }
        }

        /// <summary>
        /// Whether effects should be supressed.
        /// </summary>
        public bool SuppressEffects
        {
            get
            {
                lock (DataLock)
                {
                    return m_suppressEffects;
                }
            }

            set
            {
                lock (DataLock)
                {
                    m_suppressEffects = value;
                }
            }
        }

        /// <summary>
        /// Whether an effect was supressed (i.e. the state has changed).
        /// </summary>
        public bool UnreportedEffect
        {
            get
            {
                lock (DataLock)
                {
                    return m_unreportedEffect;
                }
            }

            protected set
            {
                lock (DataLock)
                {
                    m_unreportedEffect = value;
                }
            }
        }

        /// <summary>
        /// Returns the initial state for the machine.
        /// </summary>
        public QualifiedName InitialStateName
        {
            get
            {
                lock (DataLock)
                {    
                    if (m_initialStateName == null)
                    {
                        return QualifiedName.Null;
                    }

                    return m_initialStateName.BrowseName;
                }
            }
        }

        /// <summary>
        /// Returns the current state of the machine.
        /// </summary>
        public QualifiedName CurrentStateName
        {
            get
            {
                lock (DataLock)
                {              
                    if (m_currentStateName == null)
                    {
                        return QualifiedName.Null;
                    }  

                    return m_currentStateName.BrowseName;
                }
            }
        }

        /// <summary>
        /// Sets the parent state when the machine is used as a substate machine.
        /// </summary>
        public QualifiedName ParentState
        {
            get
            {
                lock (DataLock)
                {                
                    return m_parentState;
                }
            }

            set
            {
                lock (DataLock)
                {               
                    m_parentState = value;
                }
            }
        }

        /// <summary>
        /// Raised when a transition is initiated.
        /// </summary>
        public event StateMachineTransitionEventHandler TransitionInitiated
        {
            add
            {
                lock (DataLock)
                {                
                    m_TransitionInitiated += value;
                }
            }

            remove
            {
                lock (DataLock)
                {                
                    m_TransitionInitiated -= value;
                }
            }
        }
        
        /// <summary>
        /// Raised when a transition is completed.
        /// </summary>
        public event StateMachineTransitionEventHandler TransitionCompleted
        {
            add
            {
                lock (DataLock)
                {                
                    m_TransitionCompleted += value;
                }
            }

            remove
            {
                lock (DataLock)
                {                
                    m_TransitionCompleted -= value;
                }
            }
        }

        /// <summary>
        /// Moves the state machine into the initial state.
        /// </summary>
        public void GotoInitialState()
        {
            lock (DataLock)
            {
                UpdateState(m_initialStateName, null);
            }
        }

        /// <summary>
        /// Updates the state after the parent state changes.
        /// </summary>
        public void ParentMachineStateChanged(QualifiedName state, QualifiedName transition)
        {
            if (state != m_parentState)
            {
                UpdateState(null, null);
            }
            else
            {
                if (m_lastState == null)
                {
                    UpdateState(m_initialStateName, null);
                }
                else
                {
                    UpdateState(m_lastState, null);
                }
            }
        }
        
        /// <summary>
        /// Sets the state for the machine.
        /// </summary>
        public virtual void SetState(OperationContext context, QualifiedName stateName)
        {
            lock (DataLock)
            {
                Transition transition = BeginTransitionToState(context, stateName);
                
                OnBeforeTransition(context, transition, null);
                
                UpdateState(transition.ToState, transition);

                EndTransition(context, transition, null);

                OnAfterTransition(context, transition, null);
            }
        }        
        
        /// <summary>
        /// Sets the state for the machine.
        /// </summary>
        public virtual void SetSubState(
            OperationContext context, 
            QualifiedName    stateName, 
            QualifiedName    substateMachine, 
            QualifiedName    substateName)
        {
            lock (DataLock)
            {
                Transition transition = BeginTransitionToState(context, stateName);
                
                OnBeforeTransition(context, transition, null);
                
                UpdateState(transition.ToState, transition);
                
                // update submachine before reporting effect.
                foreach (StateMachine submachine in m_substateMachines)
                {
                    // check for match.
                    if (substateMachine != submachine.BrowseName)
                    {
                        continue;
                    }

                    // update state.
                    State substate = null;

                    if (submachine.m_states.TryGetValue(substateName, out substate))
                    {
                        submachine.UpdateState(substate, transition);
                    }
                }            

                EndTransition(context, transition, null);

                OnAfterTransition(context, transition, null);
            }
        }    

        /// <summary>
        /// Sets the state for the machine without generating any events.
        /// </summary>
        public virtual void ForceState(QualifiedName stateName)
        {
            lock (DataLock)
            {
                State state = null;

                if (!m_states.TryGetValue(stateName, out state))
                {
                    throw ServiceResultException.Create(StatusCodes.BadBrowseNameInvalid, "Must specify a valid state.");
                }
                
                UpdateState(state, null);
            }
        }    

        /// <summary>
        /// Called when a cause is invoked.
        /// </summary>
        public virtual void OnCauseInvoked(OperationContext context, QualifiedName causeName)
        {
            lock (DataLock)
            {
                Transition transition = null;

                try
                {
                    transition = BeginTransitionForCause(context, causeName);
                    
                    OnBeforeTransition(context, transition, causeName);
                    
                    UpdateState(transition.ToState, transition);

                    EndTransition(context, transition, causeName);

                    ReportAuditEvent(context, transition, causeName, null);

                    OnAfterTransition(context, transition, causeName);
                }
                catch (Exception e)
                {
                    ReportAuditEvent(context, transition, causeName, e);
                    throw ServiceResultException.Create(StatusCodes.BadMethodInvalid, e, e.Message);
                } 
            }
        }
        
        /// <summary>
        /// Begins a transition.
        /// </summary>
        protected virtual Transition BeginTransitionToState(OperationContext context, QualifiedName toState)
        {
            if (m_currentState == null || m_currentStateName == null)
            {
                GotoInitialState();
            }

            foreach (Transition transition in m_transitions)
            {
                if (transition.FromState == m_currentStateName && transition.ToState.BrowseName == toState)
                {        
                    return transition;
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadMethodInvalid,
                "Cannot move to {0} State while in the {1} State.",
                toState.Name,
                CurrentStateName);
        }

        /// <summary>
        /// Begins a transition.
        /// </summary>
        protected virtual Transition BeginTransitionForCause(OperationContext context, QualifiedName causeName)
        {
            foreach (Transition transition in m_transitions)
            {
                if (transition.FromState == m_currentStateName)
                {        
                    foreach (MethodSource cause in transition.Causes)
                    {
                        if (cause.BrowseName == causeName)
                        {
                            return transition;
                        }
                    }
                }
            }

            throw ServiceResultException.Create(
                StatusCodes.BadMethodInvalid,
                "Method {0} cannot be used while in the {1} State.",
                causeName.Name,
                m_currentStateName.DisplayName);
        }
        
        /// <summary>
        /// Does any processing before a transition occurs.
        /// </summary>
        protected virtual void OnBeforeTransition(OperationContext context, Transition transition, QualifiedName cause)
        {
            // raise a notification that a transition is about to occur.
            if (m_TransitionInitiated != null)
            {
                StateMachineTransitionEventArgs args = new StateMachineTransitionEventArgs(transition.FromState.BrowseName, transition.ToState.BrowseName, cause);
                
                m_TransitionInitiated(context, this, args);

                if (args.Cancel)
                {
                    throw ServiceResultException.Create(StatusCodes.Bad, "Transition to State '{0}' was cancelled because: '{1}'.", transition.ToState.DisplayName, args.CancelReason);
                }
            }
        }
        
        /// <summary>
        /// Does any processing after a transition occurs.
        /// </summary>
        protected virtual void OnAfterTransition(OperationContext context, Transition transition, QualifiedName cause)
        {
            // raise a notification that a transition has occurred.
            if (m_TransitionCompleted != null)
            {
                StateMachineTransitionEventArgs args = new StateMachineTransitionEventArgs(
                    transition.FromState.BrowseName, 
                    transition.ToState.BrowseName, 
                    cause);

                m_TransitionCompleted(context, this, args);
            }
        }
        
        /// <summary>
        /// Ends a transition (reports events for all effects).
        /// </summary>
        protected virtual void EndTransition(OperationContext context, Transition transition, QualifiedName cause)
        {
            if (transition != null)
            {
                foreach (NodeId effectId in transition.Effects)
                {
                    // check if the effects are being surpressed.
                    if (m_suppressEffects)
                    {
                        m_unreportedEffect = true;
                        return;
                    }

                    ReportEffect(context, transition, cause, effectId);
                }
            }
        }

        /// <summary>
        /// Finds the parent that has the specified type.
        /// </summary>
        protected NodeSource FindContainer(Type containerType)
        {
            NodeSource parent = this.Parent;

            while (parent != null)
            {
                if (containerType.IsInstanceOfType(parent))
                {
                    return parent;
                }

                parent = parent.Parent;
            }

            return null;
        }

        /// <summary>
        /// Reports an effect which is an effect of a transition.
        /// </summary>
        protected virtual void ReportEffect(
            OperationContext context, 
            Transition       transition, 
            QualifiedName    cause,
            NodeId           effectId)
        {
            if (effectId == ObjectTypes.TransitionEventType)
            {
                TransitionEvent e = TransitionEvent.Construct(Server);

                e.InitializeNewEvent();
                
                e.Message.Value       = Utils.Format("StateMachine has moved to the {0} state.", transition.ToState.DisplayName);
                e.SourceNode.Value    = NodeId;
                e.SourceName.Value    = BrowseName.Name;
                e.Severity.Value      = 10;
                e.ReceiveTime.Value   = DateTime.UtcNow;
                e.FromState.Value     = transition.FromState.DisplayName;
                e.ToState.Value       = transition.ToState.DisplayName;

                ReportEvent(e);
            }
        }
        
        /// <summary>
        /// Reports an audit event for the cause.
        /// </summary>
        protected virtual void ReportAuditEvent(OperationContext context, Transition transition, QualifiedName cause, Exception exception)
        {
            AuditUpdateStateEvent e = CreateAuditEvent(context, transition, cause, exception);
                        
            e.InitializeNewEvent();
            
            e.Message.Value            = Utils.Format("Method {0} was called.", cause);
            e.SourceNode.Value         = NodeId;
            e.SourceName.Value         = "Method/Call";
            e.Severity.Value           = 1;
            e.ReceiveTime.Value        = DateTime.UtcNow;
            e.ActionTimeStamp.Value    = DateTime.UtcNow;
            e.OldStateId.Value         = m_currentStateName.StateNumber;
            e.NewStateId.Value         = m_currentStateName.StateNumber;  

            if (context != null)
            {
                e.ClientAuditEntryId.Value = context.AuditEntryId;
                e.ClientUserId.Value       = context.Session.Identity.DisplayName;
            }

            if (transition != null)
            {
                e.OldStateId.Value = transition.FromState.StateNumber;
                e.NewStateId.Value = transition.ToState.StateNumber;
            }

            ReportEvent(e);
        }

        /// <summary>
        /// Creates an audit event for the cause.
        /// </summary>
        protected virtual AuditUpdateStateEvent CreateAuditEvent(
            OperationContext context, 
            Transition       transition, 
            QualifiedName    cause, 
            Exception        exception)
        {
            return AuditUpdateStateEvent.Construct(Server);
        }
        
        /// <summary>
        /// Updates the collapses substate name for the state machine.
        /// </summary>
        protected virtual void UpdateCollapsedSubstate()
        {
            StateMachine parent = (StateMachine)FindContainer(typeof(StateMachine));

            if (parent != null)
            {
                parent.UpdateCollapsedSubstate();
                return;
            }

            if (this.CurrentState.EffectiveDisplayNameSpecified)
            {            
                this.CurrentState.EffectiveDisplayName.Value = GetCollapsedSubstate();
            }
        }

        /// <summary>
        /// Returns the collapses substate name for the state machine.
        /// </summary>
        protected virtual LocalizedText GetCollapsedSubstate()
        {
            return null;
        }

        /// <summary>
        /// Returns the substate machines.
        /// </summary>
        protected IList<StateMachine> SubStateMachines
        {
            get
            {
                lock (DataLock)
                {
                    return m_substateMachines;
                }
            }
        }

        /// <summary>
        /// Updates the state of the machine.
        /// </summary>
        private void UpdateState(State state, Transition transition)
        {
            // change the state.
            m_lastState = m_currentStateName;
            m_currentStateName = state;

            // update the status information to reflect the new state.
            if (m_currentStateName != null)
            {
                m_currentState.DisplayName = m_currentStateName.DisplayName;
            }

            // update the status information to reflect an invalid state.
            else
            {
                m_currentState.DisplayName = String.Empty;

                // override the status code all properties.
                OverrideValueStatus(StatusCodes.UncertainLastUsableValue);
            }
            
            // save the transition information.
            if (transition != null)
            {
                //m_lastTransition.BrowseName.Value = transition.BrowseName;
                //m_lastTransition.DisplayName.Value  = transition.DisplayName;
            }
            else
            {
                //m_lastTransition.BrowseName.Value = QualifiedName.Null;
                //m_lastTransitionDisplayName.Value  = String.Empty;
            }
            
            //m_timeOfLastTransition.Value = DateTime.UtcNow;

            // get the list of available causes.
            Dictionary<QualifiedName,MethodSource> methods = new Dictionary<QualifiedName,MethodSource>();

            foreach (Transition current in m_transitions)
            {                
                if (!Object.ReferenceEquals(current.FromState, m_currentStateName))
                {
                    continue;
                }

                foreach (MethodSource cause in current.Causes)
                {                
                    methods[cause.BrowseName] = cause;
                }
            }

            // set executable bit for the available causes 
            foreach (MethodSource cause in m_causes.Values)
            {
                cause.Executable = methods.ContainsKey(cause.BrowseName);
                cause.UserExecutable = cause.Executable;
            }

            // update submachines.
            foreach (StateMachine submachine in m_substateMachines)
            {
                QualifiedName stateName = (m_currentStateName != null)?m_currentStateName.BrowseName:null;
                QualifiedName transitionName = (transition != null)?transition.BrowseName:null;

                submachine.ParentMachineStateChanged(stateName, transitionName);
            }
            
            // update collapsed substate after updating statemachines.
            UpdateCollapsedSubstate();
        }
        #endregion

        #region State Class
        /// <summary>
        /// Caches information associated with a state.
        /// </summary>
        protected class State
        {
            /// <summary>
            /// Constructs a new instance.
            /// </summary>
            public State(ILocalNode state)
            {
                m_nodeId = state.NodeId;
                m_browseName = state.BrowseName;
                m_displayName = state.DisplayName;
            }

            /// <summary>
            /// The node id for the state.
            /// </summary>
            public NodeId NodeId
            {
                get { return m_nodeId; }
            }
            
            /// <summary>
            /// The browse name for the state.
            /// </summary>
            public QualifiedName BrowseName
            {
                get { return m_browseName; }
            }

            /// <summary>
            /// The display name for the state.
            /// </summary>
            public LocalizedText DisplayName
            {
                get { return m_displayName; }
            }

            /// <summary>
            /// The state number.
            /// </summary>
            public uint StateNumber
            {
                get { return m_stateNumber;  }
                set { m_stateNumber = value; }
            }
            
            private NodeId m_nodeId;
            private QualifiedName m_browseName;
            private LocalizedText m_displayName;
            private uint m_stateNumber;
        }
        #endregion

        #region Transition Class
        /// <summary>
        /// Caches information associated with a transition.
        /// </summary>
        protected class Transition
        {
            /// <summary>
            /// Constructs a new instance.
            /// </summary>
            public Transition(ILocalNode transition)
            {
                m_nodeId = transition.NodeId;
                m_browseName = transition.BrowseName;
                m_displayName = transition.DisplayName;
                m_effects = new List<NodeId>();
                m_causes = new List<MethodSource>();
            }

            /// <summary>
            /// The node id for the state.
            /// </summary>
            public NodeId NodeId
            {
                get { return m_nodeId; }
            }            
            
            /// <summary>
            /// The browse name for the transition.
            /// </summary>
            public QualifiedName BrowseName
            {
                get { return m_browseName; }
            }

            /// <summary>
            /// The diplay name for the transition.
            /// </summary>
            public LocalizedText DisplayName
            {
                get { return m_displayName; }
            }
            
            /// <summary>
            /// The from state.
            /// </summary>
            public State FromState
            {
                get { return m_fromState; }
                set { m_fromState = value; }
            }
            
            
            /// <summary>
            /// The to state.
            /// </summary>
            public State ToState
            {
                get { return m_toState; }
                set { m_toState = value; }
            }            
            
            /// <summary>
            /// The name of the from state.
            /// </summary>
            public IList<NodeId> Effects
            {
                get { return m_effects; }
            }

            /// <summary>
            /// 
            /// </summary>
            public IList<MethodSource> Causes
            {
                get { return m_causes; }
            }
            
            private NodeId m_nodeId;
            private QualifiedName m_browseName;
            private LocalizedText m_displayName;
            private State m_fromState;
            private State m_toState;
            private List<NodeId> m_effects;
            private List<MethodSource> m_causes;
        }
        #endregion

        #region Private Fields
        private bool m_suppressEffects;
        private bool m_unreportedEffect;
        private State m_initialStateName;
        private Dictionary<QualifiedName,State> m_states;
        private List<Transition> m_transitions;
        private Dictionary<QualifiedName,MethodSource> m_causes;
        private QualifiedName m_parentState;
        private State m_currentStateName;
        private State m_lastState;
        private List<StateMachine> m_substateMachines;
        private event StateMachineTransitionEventHandler m_TransitionInitiated;
        private event StateMachineTransitionEventHandler m_TransitionCompleted;
        #endregion
    }

    #region StateMachineTransitionEventArgs Class
    /// <summary>
    /// The event arguments provided when a state machine transition occurs.
    /// </summary>
    public class StateMachineTransitionEventArgs : EventArgs
    {
        #region Constructors
        /// <summary>
        /// Creates a new instance.
        /// </summary>
        internal StateMachineTransitionEventArgs(QualifiedName fromState, QualifiedName toState, QualifiedName cause)
        {
            m_fromState = fromState;
            m_toState = toState;
            m_cause = cause;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The starting state for the transition.
        /// </summary>
        public QualifiedName FromState
        {
            get { return m_fromState; }
        }

        /// <summary>
        /// The destination state for the transition.
        /// </summary>
        public QualifiedName ToState
        {
            get { return m_toState; }
        }

        /// <summary>
        /// The cause of the transition.
        /// </summary>
        public QualifiedName Cause
        {
            get { return m_cause; }
        }

        /// <summary>
        /// Whether the transition should be cancelled.
        /// </summary>
        public bool Cancel
        {
            get { return m_cancel;  }
            set { m_cancel = value; }
        }

        /// <summary>
        /// The reason for cancelling the transition.
        /// </summary>
        public ServiceResult CancelReason
        {
            get { return m_cancelReason;  }
            set { m_cancelReason = value; }
        }
        #endregion
        
        #region Private Fields
        private QualifiedName m_fromState;
        private QualifiedName m_toState;
        private QualifiedName m_cause;
        private bool m_cancel;
        private ServiceResult m_cancelReason;
        #endregion
    }

    /// <summary>
    /// Used handle transitions between states.
    /// </summary>
    public delegate void StateMachineTransitionEventHandler(OperationContext context, StateMachine machine, StateMachineTransitionEventArgs e);
    #endregion
#endif
}
