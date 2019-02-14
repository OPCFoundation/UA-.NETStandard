/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using Opc.Ua;

namespace Quickstarts.AlarmConditionServer
{    
    /// <summary>
    /// Maps an alarm source to a UA object node.
    /// </summary>
    public partial class SourceState : BaseObjectState
    {
        #region Constructors
        /// <summary>
        /// Initializes the area.
        /// </summary>
        public SourceState(
            QuickstartNodeManager nodeManager,
            NodeId nodeId,
            string sourcePath) 
        : 
            base(null)
        {
            Initialize(nodeManager.SystemContext);

            // save the node manager that owns the source.
            m_nodeManager = nodeManager;

            // create the source with the underlying system.
            m_source = ((UnderlyingSystem)nodeManager.SystemContext.SystemHandle).CreateSource(sourcePath, OnAlarmChanged);
            
            // initialize the area with the fixed metadata.
            this.SymbolicName = m_source.Name;
            this.NodeId = nodeId;
            this.BrowseName = new QualifiedName(Utils.Format("{0}", m_source.Name), nodeId.NamespaceIndex);
            this.DisplayName = BrowseName.Name;
            this.Description = null;
            this.ReferenceTypeId = null;
            this.TypeDefinitionId = ObjectTypeIds.BaseObjectType;
            this.EventNotifier = EventNotifiers.None;

            // create a dialog.
            m_dialog = CreateDialog("OnlineState");

            // create the table of conditions.
            m_alarms = new Dictionary<string, AlarmConditionState>();
            m_events = new Dictionary<string, AlarmConditionState>();
            m_branches = new Dictionary<NodeId, AlarmConditionState>();
            
            // request an updated for all alarms.
            m_source.Refresh();
        }
        #endregion

        #region Public Interface
        /// <summary>
        /// Returns the last event produced for any conditions belonging to the node or its chilren.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="events">The list of condition events to return.</param>
        /// <param name="includeChildren">Whether to recursively report events for the children.</param>
        public override void ConditionRefresh(ISystemContext context, List<IFilterTarget> events, bool includeChildren)
        {
            // need to check if this source has already been processed during this refresh operation.
            for (int ii = 0; ii < events.Count; ii++)
            {
                InstanceStateSnapshot e = events[ii] as InstanceStateSnapshot;

                if (e != null && Object.ReferenceEquals(e.Handle, this))
                {
                    return;
                }
            }

            // report the dialog.
            if (m_dialog != null)
            {
                // do not refresh dialogs that are not active.
                if (m_dialog.Retain.Value)
                {
                    // create a snapshot.
                    InstanceStateSnapshot e = new InstanceStateSnapshot();
                    e.Initialize(context, m_dialog);

                    // set the handle of the snapshot to check for duplicates.
                    e.Handle = this;

                    events.Add(e);
                }
            }

            // the alarm objects act as a cache for the last known state and are used to generate refresh events.
            foreach (AlarmConditionState alarm in m_alarms.Values)
            {
                // do not refresh alarms that are not in an interesting state.
                if (!alarm.Retain.Value)
                {
                    continue;
                }

                // create a snapshot.
                InstanceStateSnapshot e = new InstanceStateSnapshot();
                e.Initialize(context, alarm);

                // set the handle of the snapshot to check for duplicates.
                e.Handle = this;

                events.Add(e);
            }

            // report any active branches.
            foreach (AlarmConditionState alarm in m_branches.Values)
            {
                // create a snapshot.
                InstanceStateSnapshot e = new InstanceStateSnapshot();
                e.Initialize(context, alarm);

                // set the handle of the snapshot to check for duplicates.
                e.Handle = this;

                events.Add(e);
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Called when the state of an alarm for the source has changed.
        /// </summary>
        private void OnAlarmChanged(UnderlyingSystemAlarm alarm)
        {
            lock (m_nodeManager.Lock)
            {
                // ignore archived alarms for now.
                if (alarm.RecordNumber != 0)
                {
                    NodeId branchId = new NodeId(alarm.RecordNumber, this.NodeId.NamespaceIndex);
                    
                    // find the alarm branch.
                    AlarmConditionState branch = null;

                    if (!m_branches.TryGetValue(alarm.Name, out branch))
                    {
                        m_branches[branchId] = branch = CreateAlarm(alarm, branchId);
                    }

                    // map the system information to the UA defined alarm.
                    UpdateAlarm(branch, alarm);
                    ReportChanges(branch);

                    // delete the branch.
                    if ((alarm.State & UnderlyingSystemAlarmStates.Deleted) != 0)
                    {
                        m_branches.Remove(branchId);
                    }

                    return;
                }

                // find the alarm node.
                AlarmConditionState node = null;

                if (!m_alarms.TryGetValue(alarm.Name, out node))
                {
                    m_alarms[alarm.Name] = node = CreateAlarm(alarm, null);
                }

                // map the system information to the UA defined alarm.
                UpdateAlarm(node, alarm);
                ReportChanges(node);
            }
        }
        
        /// <summary>
        /// Creates a new dialog condition
        /// </summary>
        private DialogConditionState CreateDialog(string dialogName)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            DialogConditionState node = new DialogConditionState(this);

            node.SymbolicName = dialogName;

            // specify optional fields.
            node.EnabledState = new TwoStateVariableState(node);
            node.EnabledState.TransitionTime = new PropertyState<DateTime>(node.EnabledState);
            node.EnabledState.EffectiveDisplayName = new PropertyState<LocalizedText>(node.EnabledState);
            node.EnabledState.Create(context, null, BrowseNames.EnabledState, null, false);

            // specify reference type between the source and the alarm.
            node.ReferenceTypeId = ReferenceTypeIds.HasComponent;

            // This call initializes the condition from the type model (i.e. creates all of the objects
            // and variables requried to store its state). The information about the type model was 
            // incorporated into the class when the class was created.
            node.Create(
                context,
                null,
                new QualifiedName(dialogName, this.BrowseName.NamespaceIndex),
                null,
                true);

            this.AddChild(node);

            // initialize event information.
            node.EventId.Value = Guid.NewGuid().ToByteArray();
            node.EventType.Value = node.TypeDefinitionId;
            node.SourceNode.Value = this.NodeId;
            node.SourceName.Value = this.SymbolicName;
            node.ConditionName.Value = node.SymbolicName;
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;
            node.LocalTime.Value = Utils.GetTimeZoneInfo();
            node.Message.Value = "The dialog was activated";
            node.Retain.Value = true;

            node.SetEnableState(context, true);
            node.SetSeverity(context, EventSeverity.Low);

            // initialize the dialog information.
            node.Prompt.Value = "Please specify a new state for the source.";
            node.ResponseOptionSet.Value = s_ResponseOptions;
            node.DefaultResponse.Value = 2;
            node.CancelResponse.Value = 2;
            node.OkResponse.Value = 0;

            // set up method handlers.
            node.OnRespond = OnRespond;

            // this flag needs to be set because the underlying system does not produce these events.
            node.AutoReportStateChanges = true;

            // activate the dialog.
            node.Activate(context);

            // return the new node.
            return node;
        }

        /// <summary>
        /// The responses used with the dialog condition.
        /// </summary>
        private LocalizedText[] s_ResponseOptions = new LocalizedText[]
        {
            "Online",
            "Offline",
            "No Change"
        };

        /// <summary>
        /// Creates a new alarm for the source.
        /// </summary>
        /// <param name="alarm">The alarm.</param>
        /// <param name="branchId">The branch id.</param>
        /// <returns>The new alarm.</returns>
        private AlarmConditionState CreateAlarm(UnderlyingSystemAlarm alarm, NodeId branchId)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            AlarmConditionState node = null;

            // need to map the alarm type to a UA defined alarm type.
            switch (alarm.AlarmType)
            {
                case "HighAlarm":
                {
                    ExclusiveDeviationAlarmState node2 = new ExclusiveDeviationAlarmState(this);
                    node = node2;
                    node2.HighLimit = new PropertyState<double>(node2);
                    break;
                }

                case "HighLowAlarm":
                {
                    NonExclusiveLevelAlarmState node2 = new NonExclusiveLevelAlarmState(this);
                    node = node2;

                    node2.HighHighLimit = new PropertyState<double>(node2);
                    node2.HighLimit = new PropertyState<double>(node2);
                    node2.LowLimit = new PropertyState<double>(node2);
                    node2.LowLowLimit = new PropertyState<double>(node2);

                    node2.HighHighState = new TwoStateVariableState(node2);
                    node2.HighState = new TwoStateVariableState(node2);
                    node2.LowState = new TwoStateVariableState(node2);
                    node2.LowLowState = new TwoStateVariableState(node2);

                    break;
                }
                    
                case "TripAlarm":
                {
                    node = new TripAlarmState(this);
                    break;
                }

                default:
                {
                    node = new AlarmConditionState(this);
                    break;
                }
            }

            node.SymbolicName = alarm.Name;

            // add optional components.
            node.Comment = new ConditionVariableState<LocalizedText>(node);
            node.ClientUserId = new PropertyState<string>(node);
            node.AddComment = new AddCommentMethodState(node);
            node.ConfirmedState = new TwoStateVariableState(node);
            node.Confirm = new AddCommentMethodState(node);

            if (NodeId.IsNull(branchId))
            {
                node.SuppressedState = new TwoStateVariableState(node);
                node.ShelvingState = new ShelvedStateMachineState(node);
            }

            // adding optional components to children is a little more complicated since the 
            // necessary initilization strings defined by the class that represents the child.
            // in this case we pre-create the child, add the optional components
            // and call create without assigning NodeIds. The NodeIds will be assigned when the
            // parent object is created.
            node.EnabledState = new TwoStateVariableState(node);
            node.EnabledState.TransitionTime = new PropertyState<DateTime>(node.EnabledState);
            node.EnabledState.EffectiveDisplayName = new PropertyState<LocalizedText>(node.EnabledState);
            node.EnabledState.Create(context, null, BrowseNames.EnabledState, null, false);

            // same procedure add optional components to the ActiveState component.
            node.ActiveState = new TwoStateVariableState(node);
            node.ActiveState.TransitionTime = new PropertyState<DateTime>(node.ActiveState);
            node.ActiveState.EffectiveDisplayName = new PropertyState<LocalizedText>(node.ActiveState);
            node.ActiveState.Create(context, null, BrowseNames.ActiveState, null, false);

            // specify reference type between the source and the alarm.
            node.ReferenceTypeId = ReferenceTypeIds.HasComponent;

            // This call initializes the condition from the type model (i.e. creates all of the objects
            // and variables requried to store its state). The information about the type model was 
            // incorporated into the class when the class was created.
            //
            // This method also assigns new NodeIds to all of the components by calling the INodeIdFactory.New
            // method on the INodeIdFactory object which is part of the system context. The NodeManager provides
            // the INodeIdFactory implementation used here.
            node.Create(
                context,
                null,
                new QualifiedName(alarm.Name, this.BrowseName.NamespaceIndex),
                null,
                true);

            // don't add branches to the address space.
            if (NodeId.IsNull(branchId))
            {
                this.AddChild(node);
            }

            // initialize event information.node
            node.EventType.Value = node.TypeDefinitionId;
            node.SourceNode.Value = this.NodeId;
            node.SourceName.Value = this.SymbolicName;
            node.ConditionName.Value = node.SymbolicName;
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;
            node.LocalTime.Value = Utils.GetTimeZoneInfo();
            node.BranchId.Value = branchId;
            
            // set up method handlers.
            node.OnEnableDisable = OnEnableDisableAlarm;
            node.OnAcknowledge = OnAcknowledge;
            node.OnAddComment = OnAddComment;
            node.OnConfirm = OnConfirm;
            node.OnShelve = OnShelve;
            node.OnTimedUnshelve = OnTimedUnshelve;

            // return the new node.
            return node;
        }

        /// <summary>
        /// Updates the alarm with a new state.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <param name="alarm">The alarm.</param>
        private void UpdateAlarm(AlarmConditionState node, UnderlyingSystemAlarm alarm)
        {
            ISystemContext context = m_nodeManager.SystemContext;

            // remove old event.
            if (node.EventId.Value != null)
            {
                m_events.Remove(Utils.ToHexString(node.EventId.Value));
            }

            // update the basic event information (include generating a unique id for the event).
            node.EventId.Value = Guid.NewGuid().ToByteArray();
            node.Time.Value = DateTime.UtcNow;
            node.ReceiveTime.Value = node.Time.Value;

            // save the event for later lookup.
            m_events[Utils.ToHexString(node.EventId.Value)] = node;
            
            // determine the retain state.
            node.Retain.Value = true;

            if (alarm != null)
            {
                node.Time.Value = alarm.Time;
                node.Message.Value = new LocalizedText(alarm.Reason);

                // update the states.
                node.SetEnableState(context, (alarm.State & UnderlyingSystemAlarmStates.Enabled) != 0);
                node.SetAcknowledgedState(context, (alarm.State & UnderlyingSystemAlarmStates.Acknowledged) != 0);
                node.SetConfirmedState(context, (alarm.State & UnderlyingSystemAlarmStates.Confirmed) != 0);
                node.SetActiveState(context, (alarm.State & UnderlyingSystemAlarmStates.Active) != 0);
                node.SetSuppressedState(context, (alarm.State & UnderlyingSystemAlarmStates.Suppressed) != 0);

                // update other information.
                node.SetComment(context, alarm.Comment, alarm.UserName);
                node.SetSeverity(context, alarm.Severity);

                node.EnabledState.TransitionTime.Value = alarm.EnableTime;
                node.ActiveState.TransitionTime.Value = alarm.ActiveTime;

                // check for deleted items.
                if ((alarm.State & UnderlyingSystemAlarmStates.Deleted) != 0)
                {
                    node.Retain.Value = false;
                }

                // handle high alarms.
                ExclusiveLimitAlarmState highAlarm = node as ExclusiveLimitAlarmState;

                if (highAlarm != null)
                {
                    highAlarm.HighLimit.Value = alarm.Limits[0];

                    if ((alarm.State & UnderlyingSystemAlarmStates.High) != 0)
                    {
                        highAlarm.SetLimitState(context, LimitAlarmStates.High);
                    }
                }

                // handle high-low alarms.
                NonExclusiveLimitAlarmState highLowAlarm = node as NonExclusiveLimitAlarmState;

                if (highLowAlarm != null)
                {
                    highLowAlarm.HighHighLimit.Value = alarm.Limits[0];
                    highLowAlarm.HighLimit.Value = alarm.Limits[1];
                    highLowAlarm.LowLimit.Value = alarm.Limits[2];
                    highLowAlarm.LowLowLimit.Value = alarm.Limits[3];

                    LimitAlarmStates limit = LimitAlarmStates.Inactive;

                    if ((alarm.State & UnderlyingSystemAlarmStates.HighHigh) != 0)
                    {
                        limit |= LimitAlarmStates.HighHigh;
                    }

                    if ((alarm.State & UnderlyingSystemAlarmStates.High) != 0)
                    {
                        limit |= LimitAlarmStates.High;
                    }

                    if ((alarm.State & UnderlyingSystemAlarmStates.Low) != 0)
                    {
                        limit |= LimitAlarmStates.Low;
                    }

                    if ((alarm.State & UnderlyingSystemAlarmStates.LowLow) != 0)
                    {
                        limit |= LimitAlarmStates.LowLow;
                    }

                    highLowAlarm.SetLimitState(context, limit);
                }
            }

            // not interested in disabled or inactive alarms.
            if (!node.EnabledState.Id.Value || !node.ActiveState.Id.Value)
            {
                node.Retain.Value = false;
            }
        }
        
        /// <summary>
        /// Called when the alarm is enabled or disabled.
        /// </summary>
        private ServiceResult OnEnableDisableAlarm(
            ISystemContext context,
            ConditionState condition,
            bool enabling)
        {
            m_source.EnableAlarm(condition.SymbolicName, enabling);
            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm has a comment added.
        /// </summary>
        private ServiceResult OnAddComment(
            ISystemContext context,
            ConditionState condition,
            byte[] eventId,
            LocalizedText comment)
        {
            AlarmConditionState alarm = FindAlarmByEventId(eventId);

            if (alarm == null)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            m_source.CommentAlarm(alarm.SymbolicName, GetRecordNumber(alarm), comment, GetUserName(context));

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is acknowledged.
        /// </summary>
        private ServiceResult OnAcknowledge(
            ISystemContext context,
            ConditionState condition,
            byte[] eventId,
            LocalizedText comment)
        {
            AlarmConditionState alarm = FindAlarmByEventId(eventId);

            if (alarm == null)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            m_source.AcknowledgeAlarm(alarm.SymbolicName, GetRecordNumber(alarm), comment, GetUserName(context));

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is confirmed.
        /// </summary>
        private ServiceResult OnConfirm(
            ISystemContext context,
            ConditionState condition,
            byte[] eventId,
            LocalizedText comment)
        {
            AlarmConditionState alarm = FindAlarmByEventId(eventId);

            if (alarm == null)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            m_source.ConfirmAlarm(alarm.SymbolicName, GetRecordNumber(alarm), comment, GetUserName(context));

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is shelved.
        /// </summary>
        private ServiceResult OnShelve(
            ISystemContext context,
            AlarmConditionState alarm,
            bool shelving,
            bool oneShot,
            double shelvingTime)
        {
            alarm.SetShelvingState(context, shelving, oneShot, shelvingTime);
            alarm.Message.Value = "The alarm shelved.";

            UpdateAlarm(alarm, null);
            ReportChanges(alarm);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the alarm is shelved.
        /// </summary>
        private ServiceResult OnTimedUnshelve(
            ISystemContext context,
            AlarmConditionState alarm)
        {
            // update the alarm state and produce and event.
            alarm.SetShelvingState(context, false, false, 0);
            alarm.Message.Value = "The timed shelving period expired.";

            UpdateAlarm(alarm, null);
            ReportChanges(alarm);

            return ServiceResult.Good;
        }

        /// <summary>
        /// Called when the dialog receives a response.
        /// </summary>
        private ServiceResult OnRespond(
            ISystemContext context,
            DialogConditionState dialog,
            int selectedResponse)
        {
            // response 0 means set the source online.
            if (selectedResponse == 0)
            {
                m_source.SetOfflineState(false);
            }

            // response 1 means set the source offine.
            if (selectedResponse == 1)
            {
                m_source.SetOfflineState(true);
            }

            // other responses mean do nothing.
            dialog.SetResponse(context, selectedResponse);

            // dialog no longer interesting once it is deactivated.
            dialog.Message.Value = "The dialog was deactivated";
            dialog.Retain.Value = false;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Reports the changes to the alarm.
        /// </summary>
        private void ReportChanges(AlarmConditionState alarm)
        {
            // report changes to node attributes.
            alarm.ClearChangeMasks(m_nodeManager.SystemContext, true);

            // check if events are being monitored for the source.
            if (this.AreEventsMonitored)
            {
                // create a snapshot.
                InstanceStateSnapshot e = new InstanceStateSnapshot();
                e.Initialize(m_nodeManager.SystemContext, alarm);

                // report the event.
                alarm.ReportEvent(m_nodeManager.SystemContext, e);
            }
        }

        /// <summary>
        /// Finds the alarm by event id.
        /// </summary>
        /// <param name="eventId">The event id.</param>
        /// <returns>The alarm. Null if not found.</returns>
        private AlarmConditionState FindAlarmByEventId(byte[] eventId)
        {
            if (eventId == null)
            {
                return null;
            }

            AlarmConditionState alarm = null;

            if (!m_events.TryGetValue(Utils.ToHexString(eventId), out alarm))
            {
                return null;
            }

            return alarm;
        }

        /// <summary>
        /// Gets the record number associated with tge alarm.
        /// </summary>
        /// <param name="alarm">The alarm.</param>
        /// <returns>The record number; 0 if the alarm is not an archived alarm.</returns>
        private uint GetRecordNumber(AlarmConditionState alarm)
        {
            if (alarm == null)
            {
                return 0;
            }

            if (alarm.BranchId == null || alarm.BranchId.Value == null)
            {
                return 0;
            }

            uint? recordNumber = alarm.BranchId.Value.Identifier as uint?;

            if (recordNumber != null)
            {
                return recordNumber.Value;
            }

            return 0;
        }

        /// <summary>
        /// Gets the user name associated with the context.
        /// </summary>
        private string GetUserName(ISystemContext context)
        {
            if (context.UserIdentity != null)
            {
                return context.UserIdentity.DisplayName;
            }

            return null;
        }
        #endregion    

        #region Private Fields
        private QuickstartNodeManager m_nodeManager;
        private UnderlyingSystemSource m_source;
        private Dictionary<string,AlarmConditionState> m_alarms;
        private Dictionary<string,AlarmConditionState> m_events;
        private Dictionary<NodeId,AlarmConditionState> m_branches;
        private DialogConditionState m_dialog;
        #endregion
    }
}
