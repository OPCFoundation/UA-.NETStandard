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
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    public partial class ConditionState
    {
        /// <summary>
        /// Called after a node is created.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);

            Enable?.OnCallMethod = OnEnableCalled;

            Disable?.OnCallMethod = OnDisableCalled;

            AddComment?.OnCall = OnAddCommentCalled;
        }

        /// <summary>
        /// Opts this condition into filtered retain (see OPC UA Part 9, B.1.4): when the
        /// value is true, a monitored item reports one final event as the condition leaves
        /// the scope of that client's where clause, so the client sees Retain go false even
        /// though the condition is unchanged on the server.
        /// </summary>
        /// <remarks>
        /// Part 9 states that the SupportsFilteredRetain Property "is only provided on the
        /// ConditionType", and the standard nodeset accordingly declares no modelling rule
        /// for it, so condition <em>instances</em> do not carry it as an address space child.
        /// This property is therefore deliberately not part of the instance child hierarchy:
        /// it is not returned by <see cref="NodeState.GetChildren"/> and cannot be reached
        /// through <c>FindChild(BrowseNames.SupportsFilteredRetain)</c>. It is the server
        /// side switch that mirrors, per condition, what the type node advertises. A server
        /// that wants clients to browse the flag exposes it on its ConditionType node, which
        /// the generated address space already builds.
        /// <para>
        /// Because the property is not a child, nothing that copies a condition by walking
        /// its children carries it. <see cref="CreateBranch"/> copies it explicitly, so
        /// branches inherit the parent's setting.
        /// </para>
        /// </remarks>
        public PropertyState<bool>? SupportsFilteredRetain
        {
            get => m_supportsFilteredRetain;
            set
            {
                if (!ReferenceEquals(m_supportsFilteredRetain, value))
                {
                    ChangeMasks |= NodeStateChangeMasks.Children;
                }

                m_supportsFilteredRetain = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether the condition will automatically report an event when a method call completes.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if the condition automatically reports ecents; otherwise, <c>false</c>.
        /// </value>
        public bool AutoReportStateChanges { get; set; }

        /// <summary>
        /// Called when one or more sub-states change state.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="displayName">The display name for the effective state.</param>
        /// <param name="transitionTime">The transition time.</param>
        public virtual void SetEffectiveSubState(
            ISystemContext context,
            LocalizedText displayName,
            DateTime transitionTime)
        {
            TwoStateVariableState enabledState = EnabledState!; // condition states always have EnabledState after construction
            enabledState.EffectiveDisplayName?.Value = displayName;

            if (enabledState.EffectiveTransitionTime != null)
            {
                if (transitionTime != DateTime.MinValue)
                {
                    enabledState.EffectiveTransitionTime.Value = transitionTime;
                }
                else
                {
                    enabledState.EffectiveTransitionTime.Value = DateTime.UtcNow;
                }
            }
        }

        /// <summary>
        /// Sets the enable state for the condition without raising events.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="enabled">If true the condition is put into the Enabled state.</param>
        /// <remarks>This method ensures all related variables are set correctly.</remarks>
        public virtual void SetEnableState(ISystemContext context, bool enabled)
        {
            if (enabled)
            {
                UpdateStateAfterEnable(context);
            }
            else
            {
                UpdateStateAfterDisable(context);
            }

            EnabledState!.Timestamp = DateTime.UtcNow; // condition states always have EnabledState after construction
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Sets the severity for the condition without raising events.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="severity">The event severity.</param>
        /// <remarks>This method ensures all related variables are set correctly.</remarks>
        public virtual void SetSeverity(ISystemContext context, EventSeverity severity)
        {
            ConditionVariableState<ushort> lastSeverity = LastSeverity!; // condition states always have LastSeverity after construction
            PropertyState<ushort> severityProp = Severity!; // condition states always have Severity after construction
            lastSeverity.Value = severityProp.Value;
            severityProp.Value = (ushort)severity;

            lastSeverity.SourceTimestamp?.Value = DateTime.UtcNow;

            lastSeverity.Timestamp = DateTime.UtcNow;
            severityProp.Timestamp = DateTime.UtcNow;
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Updates the condition after adding a comment.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="comment">The comment.</param>
        /// <param name="clientUserId">The user that added the comment.</param>
        public virtual void SetComment(
            ISystemContext context,
            LocalizedText comment,
            string clientUserId)
        {
            if (Comment != null)
            {
                Comment.Value = comment;
                Comment.SourceTimestamp?.Value = DateTime.UtcNow;

                ClientUserId?.Value = clientUserId;
            }
        }

        /// <summary>
        /// Create a branch based off the original Event
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="branchId">The Desired Branch Id</param>
        /// <returns>ConditionState newly created branch</returns>
        public virtual ConditionState? CreateBranch(ISystemContext context, NodeId branchId)
        {
            ConditionState? state = null;

            ConditionState? branchedAlarm = CreateBranchInstance();
            if (branchedAlarm != null)
            {
                branchedAlarm.Initialize(context, this);
                branchedAlarm.BranchId!.Value = branchId; // ConditionState.Initialize creates BranchId
                branchedAlarm.AutoReportStateChanges = AutoReportStateChanges;
                // SupportsFilteredRetain is not an instance child, so Initialize does not
                // carry it over with the rest of the condition. Copy it here, otherwise
                // filtered retain would be silently off for every branch.
                CopySupportsFilteredRetain(context, branchedAlarm);
                branchedAlarm.ReportStateChange(context, false);

                string postEventId = branchedAlarm.EventId!.Value.ToHexString(); // ConditionState.Initialize creates EventId

                lock (m_branchesLock)
                {
                    (m_branches ??= []).Add(postEventId, branchedAlarm);
                }

                state = branchedAlarm;
            }

            return state;
        }

        /// <summary>
        /// Gives the branch its own copy of <see cref="SupportsFilteredRetain"/>.
        /// </summary>
        /// <remarks>
        /// The flag is not part of the instance child hierarchy - see the remarks on
        /// <see cref="SupportsFilteredRetain"/> - so <c>NodeState.Initialize</c>
        /// does not carry it across when the branch is built from its parent. The branch
        /// gets a copy rather than the parent's instance so the two nodes stay separate
        /// address space objects.
        /// </remarks>
        private void CopySupportsFilteredRetain(ISystemContext context, ConditionState branch)
        {
            PropertyState<bool>? source = SupportsFilteredRetain;

            if (source == null)
            {
                branch.SupportsFilteredRetain = null;
                return;
            }

            PropertyState<bool> copy = PropertyState<bool>.With<VariantBuilder>(branch);
            copy.Create(context, source);
            branch.SupportsFilteredRetain = copy;
        }

        /// <summary>
        /// Creates a new instance of the current condition type for branching.
        /// Override in derived types to avoid reflection-based instantiation.
        /// </summary>
        /// <returns>A new instance of the same type as the current condition.</returns>
        [UnconditionalSuppressMessage("ReflectionAnalysis", "IL2072",
            Justification = "Derived ConditionState types are preserved by the server node manager registration.")]
        protected virtual ConditionState? CreateBranchInstance()
        {
            return Activator.CreateInstance(GetType(), this) as ConditionState;
        }

        /// <summary>
        /// Returns a snapshot of the current branches for this ConditionState.
        /// </summary>
        /// <remarks>
        /// Function exists because constructor is in auto generated code.
        /// Returns a snapshot copy so the caller can safely enumerate it
        /// while other threads concurrently modify the branch collection.
        /// If no branches exist, an empty dictionary is returned.
        /// </remarks>
        public Dictionary<string, ConditionState> GetBranches()
        {
            lock (m_branchesLock)
            {
                return m_branches != null
                    ? new Dictionary<string, ConditionState>(m_branches)
                    : [];
            }
        }

        /// <summary>
        /// Finds an event, whether it is the original event, or a branch
        /// </summary>
        /// <param name="eventId">Desired Event Id</param>
        /// <returns>ConditionState branch if it exists</returns>
        public virtual ConditionState? GetEventByEventId(ByteString eventId)
        {
            if (EventId!.Value == eventId) // ConditionState.Initialize creates EventId
            {
                return this;
            }

            return GetBranch(eventId);
        }

        /// <summary>
        /// Determines whether a specified branch exists, and returns it as ConditionState
        /// </summary>
        /// <param name="eventId">Desired Event Id</param>
        /// <returns>ConditionState branch if it exists</returns>
        public ConditionState? GetBranch(ByteString eventId)
        {
            lock (m_branchesLock)
            {
                if (m_branches != null)
                {
                    foreach (ConditionState branchEvent in m_branches.Values)
                    {
                        if (branchEvent.EventId!.Value == eventId) // ConditionState.Initialize creates EventId
                        {
                            return branchEvent;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Replace the Event Id of a branch, usually due to an Acknowledgement
        /// </summary>
        /// <param name="originalEventId">Event Id prior to the Acknowledgement</param>
        /// <param name="alarm">Branch, containing the updated EventId to be stored</param>
        protected void ReplaceBranchEvent(ByteString originalEventId, ConditionState alarm)
        {
            string originalKey = originalEventId.ToHexString();
            string newKey = alarm.EventId!.Value.ToHexString(); // ConditionState.Initialize creates EventId

            lock (m_branchesLock)
            {
                m_branches ??= [];
                m_branches.Remove(originalKey);

                // the branch may already be keyed by an older or its current EventId.
                RemoveBranchEntry(alarm);
                m_branches[newKey] = alarm;
            }
        }

        /// <summary>
        /// Remove a specific branch
        /// </summary>
        /// <param name="eventId">The desired event to remove</param>
        protected void RemoveBranchEvent(ByteString eventId)
        {
            string key = eventId.ToHexString();

            lock (m_branchesLock)
            {
                if (m_branches == null)
                {
                    return;
                }

                // remove every entry of the branch, not only the one keyed by this EventId.
                if (m_branches.TryGetValue(key, out ConditionState? branch))
                {
                    RemoveBranchEntry(branch);
                    return;
                }

                foreach (ConditionState candidate in m_branches.Values)
                {
                    if (candidate.EventId!.Value == eventId) // ConditionState.Initialize creates EventId
                    {
                        RemoveBranchEntry(candidate);
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Clear all branches for this event
        /// </summary>
        public void ClearBranches()
        {
            lock (m_branchesLock)
            {
                m_branches?.Clear();
            }
        }

        /// <summary>
        /// Updates the value of Retain based off all effective alarm properties
        /// ActiveState, AckedState, ConfirmedState, Branches, Enabled
        /// </summary>
        protected virtual void UpdateRetainState()
        {
            bool retainState = GetRetainState();

            PropertyState<bool> retain = Retain!; // ConditionState.Initialize creates Retain
            if (retain.Value != retainState)
            {
                retain.Value = retainState;
            }
        }

        /// <summary>
        /// Determines the desired Retain state based off Enabled state, and whether there are any branches
        /// </summary>
        /// <remarks>
        /// All implementations of this method should check the enabled state
        /// </remarks>
        protected virtual bool GetRetainState()
        {
            bool retainState = false;

            if (EnabledState!.Id!.Value) // condition states always have EnabledState/Id after construction
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                foreach (ConditionState branch in branches.Values)
                {
                    branch.UpdateRetainState();
                    if (branch.Retain!.Value) // ConditionState.Initialize creates Retain
                    {
                        retainState = true;
                    }
                }
            }

            return retainState;
        }

        /// <summary>
        /// Get the Number of Branches currently utilized by this event
        /// </summary>
        /// <returns>
        /// Int contain the number of Branches
        /// </returns>
        public virtual int GetBranchCount()
        {
            lock (m_branchesLock)
            {
                return m_branches?.Count ?? 0;
            }
        }

        /// <summary>
        /// Determines if Events are monitored for this event.  If this is a branch, then the original event is checked
        /// </summary>
        /// <returns>
        /// Boolean determining if this event is monitored, and should be reported</returns>
        public bool EventsMonitored()
        {
            bool areEventsMonitored = AreEventsMonitored;

            if (IsBranch())
            {
                areEventsMonitored = Parent!.AreEventsMonitored; // branches always have a Parent set in CreateBranch
            }

            return areEventsMonitored;
        }

        /// <summary>
        /// Raised when the condition is enabled or disabled.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public ConditionEnableEventHandler? OnEnableDisable;

        /// <summary>
        /// Raised when a comment is added to the condition.
        /// </summary>
        /// <remarks>
        /// Return code can be used to cancel the operation.
        /// </remarks>
        public ConditionAddCommentEventHandler? OnAddComment;

        /// <summary>
        /// Handles a condition refresh.
        /// </summary>
        public override void ConditionRefresh(
            ISystemContext context,
            List<IFilterTarget> events,
            bool includeChildren)
        {
            if (Retain!.Value) // ConditionState.Initialize creates Retain
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                foreach (ConditionState branch in branches.Values)
                {
                    branch.ConditionRefresh(context, events, includeChildren);
                }
                events.Add(this);
            }
        }

        /// <summary>
        /// Reports the state change for the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        /// <param name="ignoreDisabledState">if set to <c>true</c> the event is reported event if the condition is in the disabled state.</param>
        protected void ReportStateChange(ISystemContext context, bool ignoreDisabledState)
        {
            // check the disabled state.
            if (!ignoreDisabledState && !EnabledState!.Id!.Value) // condition states always have EnabledState/Id after construction
            {
                return;
            }

            if (AutoReportStateChanges)
            {
                // create a new event instance.
                PropertyState<ByteString> eventId = EventId!; // ConditionState.Initialize creates EventId
                PropertyState<DateTimeUtc> time = Time!; // ConditionState.Initialize creates Time

                // the superseded EventId keeps identifying the state it was reported for.
                ConditionState root = GetRootCondition();
                root.RecordEventId(this, eventId.Value);
                eventId.Value = Uuid.NewUuid().ToByteString();
                root.RecordEventId(this, eventId.Value);

                // the branch table is keyed by EventId: keep the entry of a branch in step
                // with its new EventId (e.g. after a comment on the branch).
                if (!ReferenceEquals(root, this))
                {
                    root.RekeyBranchIfPresent(this);
                }

                time.Value = DateTimeUtc.Now;
                ReceiveTime!.Value = time.Value; // ConditionState.Initialize creates ReceiveTime

                ClearChangeMasks(context, includeChildren: true);

                // report a state change event.
                if (EventsMonitored())
                {
                    var snapshot = new InstanceStateSnapshot();
                    snapshot.Initialize(context, this);
                    ReportEvent(context, snapshot);
                }
            }
        }

        /// <summary>
        /// Updates the effective state for the condition.
        /// </summary>
        /// <param name="context">The context.</param>
        protected virtual void UpdateEffectiveState(ISystemContext context)
        {
            SetEffectiveSubState(context, EnabledState!.Value, DateTime.MinValue); // condition states always have EnabledState after construction
        }

        /// <summary>
        /// Called when the add comment method is called.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="method">The method being called.</param>
        /// <param name="objectId">The id of the object.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        /// <returns>Any error.</returns>
        protected virtual ServiceResult OnAddCommentCalled(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ByteString eventId,
            LocalizedText comment)
        {
            ServiceResult error = ProcessBeforeAddComment(context, eventId, comment);

            if (ServiceResult.IsGood(error))
            {
                // Part 9 5.5.6: the comment belongs to the event occurrence identified by
                // the EventId. A branch comment is applied (and reported) by the branch only.
                ConditionState? branch = GetBranch(eventId) ?? ResolveEventId(eventId)?.Owner;
                if (branch != null && !ReferenceEquals(branch, this))
                {
                    return branch.OnAddCommentCalled(context, method, objectId, eventId, comment);
                }

                string? currentUserId = GetCurrentUserId(context);
                SetComment(context, comment, currentUserId ?? string.Empty);
            }

            if (EventsMonitored())
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }

                // raise the audit event.
                var e = new AuditConditionCommentEventState(null);

                var info = new TranslationInfo(
                    "AuditConditionComment",
                    "en-US",
                    "The AddComment method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);

                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/AddComment", false);

                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);
                e.SetChildValue(
                    context,
                    BrowseNames.InputArguments,
                    Variant.From(new Variant[] { eventId, comment }),
                    false);

                e.SetChildValue(context, BrowseNames.ConditionEventId, eventId, false);
                e.SetChildValue(context, BrowseNames.Comment, comment, false);

                ReportEvent(context, e);
            }

            return error;
        }

        /// <summary>
        /// Gets the current user id from the system context.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <returns>The display name for the current user.</returns>
        protected string? GetCurrentUserId(ISystemContext context)
        {
            return context?.UserId;
        }

        /// <summary>
        /// Does any processing before adding a comment to a condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
        /// <param name="comment">The comment.</param>
        protected virtual ServiceResult ProcessBeforeAddComment(
            ISystemContext context,
            ByteString eventId,
            LocalizedText comment)
        {
            if (eventId.IsEmpty)
            {
                return StatusCodes.BadEventIdUnknown;
            }

            if (!EnabledState!.Id!.Value) // condition states always have EnabledState/Id after construction
            {
                return StatusCodes.BadConditionDisabled;
            }

            // Part 9 5.5.6: comments are added to event occurrences identified by the EventId,
            // including one whose EventId was superseded by a later state change.
            if (ResolveEventId(eventId) is not { IsLive: true })
            {
                return StatusCodes.BadEventIdUnknown;
            }

            if (OnAddComment != null)
            {
                try
                {
                    return OnAddComment(context, this, eventId, comment);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error adding a comment to a Condition.");
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles the Enable method.
        /// </summary>
        protected virtual ServiceResult OnEnableCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            ServiceResult error = ProcessBeforeEnableDisable(context, true);

            if (ServiceResult.IsGood(error))
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                // Enable all branches
                foreach (ConditionState branch in branches.Values)
                {
                    branch.OnEnableCalled(context, method, inputArguments, outputArguments);
                }

                UpdateStateAfterEnable(context);
            }

            if (AreEventsMonitored)
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }

                // raise the audit event.
                var e = new AuditConditionEnableEventState(null);

                var info = new TranslationInfo(
                    "AuditConditionEnable",
                    "en-US",
                    "The Enable method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);

                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/Enable", false);
                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);

                ReportEvent(context, e);
            }

            return error;
        }

        /// <summary>
        /// Handles the Disable method.
        /// </summary>
        protected virtual ServiceResult OnDisableCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            // check that method can be called.
            ServiceResult error = ProcessBeforeEnableDisable(context, false);

            if (ServiceResult.IsGood(error))
            {
                Dictionary<string, ConditionState> branches = GetBranches();

                foreach (ConditionState branch in branches.Values)
                {
                    branch.OnDisableCalled(context, method, inputArguments, outputArguments);
                }

                UpdateStateAfterDisable(context);
            }

            // raise the audit event.
            if (AreEventsMonitored)
            {
                // report a state change event.
                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, true);
                }

                // raise the audit event.
                var e = new AuditConditionEnableEventState(null);

                var info = new TranslationInfo(
                    "AuditConditionEnable",
                    "en-US",
                    "The Disable method was called.");

                e.Initialize(
                    context,
                    this,
                    EventSeverity.Low,
                    new LocalizedText(info),
                    ServiceResult.IsGood(error),
                    DateTime.UtcNow);

                e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                e.SetChildValue(context, BrowseNames.SourceName, "Method/Disable", false);
                e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);

                ReportEvent(context, e);
            }

            return error;
        }

        /// <summary>
        /// Does any processing before a condition is enabled or disabled.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="enabling">True if the condition is being enabled.</param>
        protected virtual ServiceResult ProcessBeforeEnableDisable(
            ISystemContext context,
            bool enabling)
        {
            if (enabling && EnabledState!.Id!.Value) // condition states always have EnabledState/Id after construction
            {
                return StatusCodes.BadConditionAlreadyEnabled;
            }

            if (!enabling && !EnabledState!.Id!.Value)
            {
                return StatusCodes.BadConditionAlreadyDisabled;
            }

            if (OnEnableDisable != null)
            {
                try
                {
                    return OnEnableDisable(context, this, enabling);
                }
                catch (Exception e)
                {
                    return ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error enabling or disabling a Condition.");
                }
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Evaluates and updates the Retain state when the condition is enabled.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <remarks>
        /// This method is called by UpdateStateAfterEnable to determine the Retain value.
        /// The default implementation calls UpdateRetainState() which uses GetRetainState().
        /// Derived classes can override this method to provide custom logic for determining
        /// the Retain value when the condition is enabled.
        /// </remarks>
        protected virtual void EvaluateRetainStateOnEnable(ISystemContext context)
        {
            UpdateRetainState();
        }

        /// <summary>
        /// Updates the condition state after enabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterEnable(ISystemContext context)
        {
            var state = new TranslationInfo(
                "ConditionStateEnabled",
                "en-US",
                ConditionStateNames.Enabled);

            TwoStateVariableState enabledState = EnabledState!; // condition states always have EnabledState after construction
            enabledState.Value = new LocalizedText(state);
            enabledState.Id!.Value = true;

            enabledState.TransitionTime?.Value = DateTime.UtcNow;

            EvaluateRetainStateOnEnable(context);

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Updates the condition state after disabling.
        /// </summary>
        /// <param name="context">The system context.</param>
        protected virtual void UpdateStateAfterDisable(ISystemContext context)
        {
            var state = new TranslationInfo(
                "ConditionStateDisabled",
                "en-US",
                ConditionStateNames.Disabled);

            Retain!.Value = false; // ConditionState.Initialize creates Retain
            TwoStateVariableState enabledState = EnabledState!; // condition states always have EnabledState after construction
            enabledState.Value = new LocalizedText(state);
            enabledState.Id!.Value = false;

            enabledState.TransitionTime?.Value = DateTime.UtcNow;

            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Determines if this event is a branch
        /// </summary>
        /// <returns>true if branch</returns>
        protected bool IsBranch()
        {
            return !BranchId!.Value.IsNull; // ConditionState.Initialize creates BranchId
        }

        /// <summary>
        /// Records the EventId of a reported state change of this condition.
        /// </summary>
        public override void ReportEvent(ISystemContext context, IFilterTarget e)
        {
            RecordReportedEvent(e);
            base.ReportEvent(context, e);
        }

        /// <summary>
        /// Records the EventId of a reported state change of this condition.
        /// </summary>
        public override ValueTask ReportEventAsync(
            ISystemContext context,
            IFilterTarget e,
            CancellationToken cancellationToken = default)
        {
            RecordReportedEvent(e);
            return base.ReportEventAsync(context, e, cancellationToken);
        }

        /// <summary>
        /// Resolves the trunk or branch whose state an EventId identifies.
        /// </summary>
        /// <remarks>
        /// Part 9 5.5.6/5.7.3/5.7.4: an EventId identifies the state of the condition (or
        /// of a branch) reported with that event notification. The EventId stays valid
        /// after a later state change reported a new EventId, so superseded EventIds are
        /// resolved through the EventIds recently reported by the condition and its branches.
        /// </remarks>
        /// <param name="eventId">The EventId passed to a condition method.</param>
        /// <returns>The reported event, or null if the EventId is not known.</returns>
        private protected ReportedConditionEvent? ResolveEventId(ByteString eventId)
        {
            ConditionState root = GetRootCondition();

            ConditionState? current = GetEventByEventId(eventId);
            if (current != null)
            {
                // remember the state as it is before the method changes it, so the
                // EventId still refers to it once a later state change supersedes it.
                root.RecordEventId(current, eventId);
                return new ReportedConditionEvent(current, true, true, 0, 0);
            }

            ReportedConditionEvent? reported;
            lock (root.m_reportedEventIdsLock)
            {
                if (root.m_reportedEventIds == null ||
                    !root.m_reportedEventIds.TryGetValue(eventId, out reported))
                {
                    return null;
                }
            }

            bool isLive = ReferenceEquals(reported.Owner, root) || ReferenceEquals(reported.Owner, this);
            if (!isLive)
            {
                lock (root.m_branchesLock)
                {
                    isLive = root.m_branches != null && root.m_branches.ContainsValue(reported.Owner);
                }
            }

            return reported with { IsCurrent = false, IsLive = isLive };
        }

        /// <summary>
        /// Returns the condition that owns the branch table: the trunk for a branch, else this condition.
        /// </summary>
        private ConditionState GetRootCondition()
        {
            return BranchId is { Value.IsNull: false } && Parent is ConditionState trunk ? trunk : this;
        }

        /// <summary>
        /// Records the EventId of a reported event of this condition.
        /// </summary>
        private void RecordReportedEvent(IFilterTarget e)
        {
            if (EventId is { } eventId &&
                (ReferenceEquals(e, this) ||
                    (e is InstanceStateSnapshot snapshot && ReferenceEquals(snapshot.Handle, this))))
            {
                GetRootCondition().RecordEventId(this, eventId.Value);
            }
        }

        /// <summary>
        /// Remembers the owner of an EventId, together with the number of times the owner
        /// was acknowledged and confirmed when the EventId was reported. Keeps the first record.
        /// </summary>
        private void RecordEventId(ConditionState owner, ByteString eventId)
        {
            if (eventId.IsEmpty)
            {
                return;
            }

            lock (m_reportedEventIdsLock)
            {
                m_reportedEventIds ??= [];
                if (!m_reportedEventIds.TryAdd(
                    eventId,
                    new ReportedConditionEvent(owner, false, false, owner.m_acknowledgeCount, owner.m_confirmCount)))
                {
                    return;
                }

                (m_reportedEventIdOrder ??= new Queue<ByteString>()).Enqueue(eventId);
                while (m_reportedEventIdOrder.Count > kMaxReportedEventIds)
                {
                    m_reportedEventIds.Remove(m_reportedEventIdOrder.Dequeue());
                }
            }
        }

        /// <summary>
        /// Removes a branch from the branch table, whatever EventId it is keyed by.
        /// </summary>
        private protected void RemoveBranch(ConditionState branch)
        {
            lock (m_branchesLock)
            {
                RemoveBranchEntry(branch);
            }
        }

        /// <summary>
        /// Keys a branch by its current EventId, whatever EventId it was keyed by before.
        /// </summary>
        private protected void RekeyBranch(ConditionState branch)
        {
            string newKey = branch.EventId!.Value.ToHexString(); // ConditionState.Initialize creates EventId

            lock (m_branchesLock)
            {
                RemoveBranchEntry(branch);
                (m_branches ??= [])[newKey] = branch;
            }
        }

        /// <summary>
        /// Re-keys a branch by its current EventId if it is in the branch table.
        /// </summary>
        private void RekeyBranchIfPresent(ConditionState branch)
        {
            string newKey = branch.EventId!.Value.ToHexString(); // ConditionState.Initialize creates EventId

            lock (m_branchesLock)
            {
                if (RemoveBranchEntry(branch))
                {
                    m_branches![newKey] = branch;
                }
            }
        }

        /// <summary>
        /// Removes every branch table entry of the branch. Caller holds the branches lock.
        /// </summary>
        /// <returns>True if the branch had an entry.</returns>
        private bool RemoveBranchEntry(ConditionState branch)
        {
            if (m_branches == null)
            {
                return false;
            }

            List<string>? keys = null;
            foreach (KeyValuePair<string, ConditionState> entry in m_branches)
            {
                if (ReferenceEquals(entry.Value, branch))
                {
                    (keys ??= []).Add(entry.Key);
                }
            }

            if (keys == null)
            {
                return false;
            }

            foreach (string key in keys)
            {
                m_branches.Remove(key);
            }

            return true;
        }

        /// <summary>
        /// A reported EventId of the condition or one of its branches.
        /// </summary>
        /// <param name="Owner">The trunk or branch that reported the EventId.</param>
        /// <param name="IsCurrent">True if the EventId is the current EventId of the owner.</param>
        /// <param name="IsLive">True if the owner is the condition or one of its current branches.</param>
        /// <param name="AcknowledgeCount">The acknowledgements of the owner when the EventId was reported.</param>
        /// <param name="ConfirmCount">The confirmations of the owner when the EventId was reported.</param>
        private protected sealed record class ReportedConditionEvent(
            ConditionState Owner,
            bool IsCurrent,
            bool IsLive,
            uint AcknowledgeCount,
            uint ConfirmCount)
        {
            /// <summary>
            /// True if the owner was acknowledged after the EventId was reported.
            /// </summary>
            public bool AcknowledgedSince => !IsCurrent && Owner.m_acknowledgeCount != AcknowledgeCount;

            /// <summary>
            /// True if the owner was confirmed after the EventId was reported.
            /// </summary>
            public bool ConfirmedSince => !IsCurrent && Owner.m_confirmCount != ConfirmCount;
        }

        /// <summary>
        /// Branches
        /// </summary>
        protected Dictionary<string, ConditionState>? m_branches;

        /// <summary>
        /// Lock protecting all access to <see cref="m_branches"/>.
        /// </summary>
        protected readonly Lock m_branchesLock = new();

        /// <summary>
        /// The number of times this condition was acknowledged.
        /// </summary>
        private protected uint m_acknowledgeCount;

        /// <summary>
        /// The number of times this condition was confirmed.
        /// </summary>
        private protected uint m_confirmCount;

        private const int kMaxReportedEventIds = 64;
        private readonly Lock m_reportedEventIdsLock = new();
        private Dictionary<ByteString, ReportedConditionEvent>? m_reportedEventIds;
        private Queue<ByteString>? m_reportedEventIdOrder;
        private PropertyState<bool>? m_supportsFilteredRetain;
    }

    /// <summary>
    /// Used to receive notifications when a condition is enabled or disabled.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="condition">The condition that raised the event.</param>
    /// <param name="enabling">True if the condition is moving/has moved to the Enabled state.</param>
    public delegate ServiceResult ConditionEnableEventHandler(
        ISystemContext context,
        ConditionState condition,
        bool enabling);

    /// <summary>
    /// Used to receive notifications when a comment is added.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="condition">The condition that raised the event.</param>
    /// <param name="eventId">The identifier for the event which is the target for the comment.</param>
    /// <param name="comment">The comment.</param>
    public delegate ServiceResult ConditionAddCommentEventHandler(
        ISystemContext context,
        ConditionState condition,
        ByteString eventId,
        LocalizedText comment);
}
