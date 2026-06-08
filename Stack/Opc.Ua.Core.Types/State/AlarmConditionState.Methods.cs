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

namespace Opc.Ua
{
    // Part 9 alarm method handlers: Silence, Suppress/Unsuppress, OutOfService,
    // PlaceInService/RemoveFromService, Reset, GetGroupMemberships
    public partial class AlarmConditionState
    {
        /// <summary>
        /// Raised when silence is requested. Return non-Good to veto.
        /// </summary>
        public AlarmConditionSimpleEventHandler? OnSilenceRequested;

        /// <summary>
        /// Raised when suppress/unsuppress is requested. Return non-Good to veto.
        /// </summary>
        public AlarmConditionSuppressEventHandler? OnSuppressRequested;

        /// <summary>
        /// Raised when out-of-service state change is requested. Return non-Good to veto.
        /// </summary>
        public AlarmConditionOutOfServiceEventHandler? OnOutOfServiceRequested;

        /// <summary>
        /// Raised when a latched alarm reset is requested. Return non-Good to veto.
        /// </summary>
        public AlarmConditionSimpleEventHandler? OnResetRequested;

        /// <summary>
        /// Sets the silence state of the alarm.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="silenced">If <c>true</c>, the alarm is silenced.</param>
        public virtual void SetSilenceState(ISystemContext context, bool silenced)
        {
            if (SilenceState is not { } silenceState)
            {
                return;
            }

            TranslationInfo state = silenced
                ? new TranslationInfo("ConditionStateSilenced", "en-US", ConditionStateNames.Silenced)
                : new TranslationInfo("ConditionStateNotSilenced", "en-US", ConditionStateNames.NotSilenced);

            silenceState.Value = new LocalizedText(state);
            silenceState.Id!.Value = silenced; // Id is created with SilenceState

            silenceState.TransitionTime?.Value = DateTimeUtc.Now;
            silenceState.Timestamp = DateTimeUtc.Now;

            UpdateEffectiveState(context);
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Sets the out-of-service state of the alarm.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="outOfService">If <c>true</c>, the alarm is out of service.</param>
        public virtual void SetOutOfServiceState(ISystemContext context, bool outOfService)
        {
            if (OutOfServiceState is not { } outOfServiceState)
            {
                return;
            }

            TranslationInfo state = outOfService
                ? new TranslationInfo("ConditionStateOutOfService", "en-US", ConditionStateNames.OutOfService)
                : new TranslationInfo("ConditionStateInService", "en-US", ConditionStateNames.InService);

            outOfServiceState.Value = new LocalizedText(state);
            outOfServiceState.Id!.Value = outOfService; // Id is created with OutOfServiceState

            outOfServiceState.TransitionTime?.Value = DateTimeUtc.Now;
            outOfServiceState.Timestamp = DateTimeUtc.Now;

            // OutOfServiceState participates in SuppressedOrShelved (Part 9 §5.8.2)
            if (outOfService)
            {
                SuppressedOrShelved!.Value = true; // SuppressedOrShelved is created with the alarm
            }
            else
            {
                // Only clear SuppressedOrShelved if not suppressed and not shelved
                if ((SuppressedState == null || !SuppressedState.Id!.Value) &&
                    (ShelvingState == null ||
                     ShelvingState.CurrentState!.Id!.Value == ObjectIds.ShelvedStateMachineType_Unshelved))
                {
                    SuppressedOrShelved!.Value = false;
                }
            }

            UpdateEffectiveState(context);
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Sets the latched state of the alarm.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="latched">If <c>true</c>, the alarm is latched.</param>
        public virtual void SetLatchedState(ISystemContext context, bool latched)
        {
            if (LatchedState is not { } latchedState)
            {
                return;
            }

            TranslationInfo state = latched
                ? new TranslationInfo("ConditionStateLatched", "en-US", ConditionStateNames.Latched)
                : new TranslationInfo("ConditionStateUnlatched", "en-US", ConditionStateNames.Unlatched);

            latchedState.Value = new LocalizedText(state);
            latchedState.Id!.Value = latched; // Id is created with LatchedState

            latchedState.TransitionTime?.Value = DateTimeUtc.Now;
            latchedState.Timestamp = DateTimeUtc.Now;

            UpdateEffectiveState(context);
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Determines whether the alarm is in a state that prevents clearing
        /// <see cref="SuppressedOrShelved"/>. Returns <c>true</c> when
        /// any of suppressed, shelved, or out-of-service is true.
        /// </summary>
        internal bool IsSuppressedOrShelvedOrOutOfService()
        {
            if (SuppressedState is { } s && s.Id!.Value)
            {
                return true;
            }

            if (OutOfServiceState is { } o && o.Id!.Value)
            {
                return true;
            }

            if (ShelvingState != null &&
                ShelvingState.CurrentState!.Id!.Value != ObjectIds.ShelvedStateMachineType_Unshelved)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Wires the Part 9 alarm methods introduced in this partial file.
        /// Called from <see cref="OnAfterCreate"/>.
        /// </summary>
        private void WireAlarmMethods()
        {
            Silence?.OnCallMethod = OnSilenceCalled;
            Suppress?.OnCallMethod = OnSuppressCalled;
            Suppress2?.OnCall = OnSuppress2Called;
            Unsuppress?.OnCallMethod = OnUnsuppressCalled;
            Unsuppress2?.OnCall = OnUnsuppress2Called;
            RemoveFromService?.OnCallMethod = OnRemoveFromServiceCalled;
            RemoveFromService2?.OnCall = OnRemoveFromService2Called;
            PlaceInService?.OnCallMethod = OnPlaceInServiceCalled;
            PlaceInService2?.OnCall = OnPlaceInService2Called;
            Reset?.OnCallMethod = OnResetCalled;
            Reset2?.OnCall = OnReset2Called;
            GetGroupMemberships?.OnCall = OnGetGroupMembershipsCalled;
        }

        /// <summary>
        /// Handles the Silence method call.
        /// </summary>
        protected virtual ServiceResult OnSilenceCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return HandleAlarmMethod(
                context, method,
                comment: default,
                "AuditConditionSilence",
                "The Silence method was called.",
                "Method/Silence",
                validate: () =>
                {
                    if (SilenceState == null)
                    {
                        return StatusCodes.BadNotSupported;
                    }

                    if (SilenceState.Id!.Value)
                    {
                        return StatusCodes.BadNothingToDo;
                    }

                    return ServiceResult.Good;
                },
                execute: (c, comment) =>
                {
                    ServiceResult? result = OnSilenceRequested?.Invoke(c, this);
                    if (result != null && ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    SetSilenceState(c, true);
                    return ServiceResult.Good;
                },
                createAuditEvent: () => new AuditConditionSilenceEventState(null));
        }

        /// <summary>
        /// Handles the Suppress method call.
        /// </summary>
        protected virtual ServiceResult OnSuppressCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return HandleSuppressCore(context, method, comment: default);
        }

        /// <summary>
        /// Handles the Suppress2 method call (with comment).
        /// </summary>
        protected virtual ServiceResult OnSuppress2Called(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            LocalizedText comment)
        {
            return HandleSuppressCore(context, method, comment);
        }

        private ServiceResult HandleSuppressCore(
            ISystemContext context,
            MethodState method,
            LocalizedText comment)
        {
            return HandleAlarmMethod(
                context, method, comment,
                "AuditConditionSuppress",
                "The Suppress method was called.",
                "Method/Suppress",
                validate: () =>
                {
                    if (SuppressedState == null)
                    {
                        return StatusCodes.BadNotSupported;
                    }

                    if (SuppressedState.Id!.Value)
                    {
                        return StatusCodes.BadNothingToDo;
                    }

                    return ServiceResult.Good;
                },
                execute: (c, cmt) =>
                {
                    ServiceResult? result = OnSuppressRequested?.Invoke(c, this, true);
                    if (result != null && ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    SetSuppressedState(c, true);
                    ApplyComment(c, cmt);
                    return ServiceResult.Good;
                },
                createAuditEvent: () => new AuditConditionSuppressionEventState(null));
        }

        /// <summary>
        /// Handles the Unsuppress method call.
        /// </summary>
        protected virtual ServiceResult OnUnsuppressCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return HandleUnsuppressCore(context, method, comment: default);
        }

        /// <summary>
        /// Handles the Unsuppress2 method call (with comment).
        /// </summary>
        protected virtual ServiceResult OnUnsuppress2Called(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            LocalizedText comment)
        {
            return HandleUnsuppressCore(context, method, comment);
        }

        private ServiceResult HandleUnsuppressCore(
            ISystemContext context,
            MethodState method,
            LocalizedText comment)
        {
            return HandleAlarmMethod(
                context, method, comment,
                "AuditConditionUnsuppress",
                "The Unsuppress method was called.",
                "Method/Unsuppress",
                validate: () =>
                {
                    if (SuppressedState == null)
                    {
                        return StatusCodes.BadNotSupported;
                    }

                    if (!SuppressedState.Id!.Value)
                    {
                        return StatusCodes.BadNothingToDo;
                    }

                    return ServiceResult.Good;
                },
                execute: (c, cmt) =>
                {
                    ServiceResult? result = OnSuppressRequested?.Invoke(c, this, false);
                    if (result != null && ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    SetSuppressedState(c, false);
                    ApplyComment(c, cmt);
                    return ServiceResult.Good;
                },
                createAuditEvent: () => new AuditConditionSuppressionEventState(null));
        }

        /// <summary>
        /// Handles the RemoveFromService method call.
        /// </summary>
        protected virtual ServiceResult OnRemoveFromServiceCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return HandleOutOfServiceCore(context, method, outOfService: true, comment: default);
        }

        /// <summary>
        /// Handles the RemoveFromService2 method call (with comment).
        /// </summary>
        protected virtual ServiceResult OnRemoveFromService2Called(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            LocalizedText comment)
        {
            return HandleOutOfServiceCore(context, method, outOfService: true, comment);
        }

        /// <summary>
        /// Handles the PlaceInService method call.
        /// </summary>
        protected virtual ServiceResult OnPlaceInServiceCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return HandleOutOfServiceCore(context, method, outOfService: false, comment: default);
        }

        /// <summary>
        /// Handles the PlaceInService2 method call (with comment).
        /// </summary>
        protected virtual ServiceResult OnPlaceInService2Called(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            LocalizedText comment)
        {
            return HandleOutOfServiceCore(context, method, outOfService: false, comment);
        }

        private ServiceResult HandleOutOfServiceCore(
            ISystemContext context,
            MethodState method,
            bool outOfService,
            LocalizedText comment)
        {
            string auditName = outOfService ? "AuditConditionRemoveFromService" : "AuditConditionPlaceInService";
            string auditMsg = outOfService
                ? "The RemoveFromService method was called."
                : "The PlaceInService method was called.";
            string sourceName = outOfService ? "Method/RemoveFromService" : "Method/PlaceInService";

            return HandleAlarmMethod(
                context, method, comment,
                auditName, auditMsg, sourceName,
                validate: () =>
                {
                    if (OutOfServiceState == null)
                    {
                        return StatusCodes.BadNotSupported;
                    }

                    if (OutOfServiceState.Id!.Value == outOfService)
                    {
                        return StatusCodes.BadNothingToDo;
                    }

                    return ServiceResult.Good;
                },
                execute: (c, cmt) =>
                {
                    ServiceResult? result = OnOutOfServiceRequested?.Invoke(c, this, outOfService);
                    if (result != null && ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    SetOutOfServiceState(c, outOfService);
                    ApplyComment(c, cmt);
                    return ServiceResult.Good;
                },
                createAuditEvent: () => new AuditConditionOutOfServiceEventState(null));
        }

        /// <summary>
        /// Handles the Reset method call.
        /// </summary>
        protected virtual ServiceResult OnResetCalled(
            ISystemContext context,
            MethodState method,
            ArrayOf<Variant> inputArguments,
            List<Variant> outputArguments)
        {
            return HandleResetCore(context, method, comment: default);
        }

        /// <summary>
        /// Handles the Reset2 method call (with comment).
        /// </summary>
        protected virtual ServiceResult OnReset2Called(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            LocalizedText comment)
        {
            return HandleResetCore(context, method, comment);
        }

        private ServiceResult HandleResetCore(
            ISystemContext context,
            MethodState method,
            LocalizedText comment)
        {
            return HandleAlarmMethod(
                context, method, comment,
                "AuditConditionReset",
                "The Reset method was called.",
                "Method/Reset",
                validate: () =>
                {
                    if (LatchedState == null)
                    {
                        return StatusCodes.BadNotSupported;
                    }

                    if (!LatchedState.Id!.Value)
                    {
                        // Not latched — nothing to reset
                        return StatusCodes.BadConditionNotShelved; // closest standard code for invalid state
                    }

                    if (ActiveState!.Id!.Value)
                    {
                        // Still active — cannot reset while process condition is present
                        return StatusCodes.BadInvalidState;
                    }

                    if (AckedState is { } ackedState && !ackedState.Id!.Value)
                    {
                        // Must acknowledge before reset
                        return StatusCodes.BadInvalidState;
                    }

                    if (ConfirmedState is { } confirmedState && !confirmedState.Id!.Value)
                    {
                        // Must confirm before reset
                        return StatusCodes.BadInvalidState;
                    }

                    return ServiceResult.Good;
                },
                execute: (c, cmt) =>
                {
                    ServiceResult? result = OnResetRequested?.Invoke(c, this);
                    if (result != null && ServiceResult.IsBad(result))
                    {
                        return result;
                    }

                    SetLatchedState(c, false);
                    ApplyComment(c, cmt);
                    return ServiceResult.Good;
                },
                createAuditEvent: () => new AuditConditionResetEventState(null));
        }

        /// <summary>
        /// Handles the GetGroupMemberships method call.
        /// </summary>
        protected virtual ServiceResult OnGetGroupMembershipsCalled(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ref ArrayOf<NodeId> groups)
        {
            if (!EnabledState!.Id!.Value)
            {
                return StatusCodes.BadConditionDisabled;
            }

            // Browse inverse AlarmGroupMember references to find groups this alarm belongs to.
            var groupList = new List<NodeId>();
            var children = new List<BaseInstanceState>();
            GetChildren(context, children);

            foreach (BaseInstanceState child in children)
            {
                if (child is FolderState folder &&
                    folder.TypeDefinitionId == ObjectTypeIds.AlarmGroupType)
                {
                    groupList.Add(folder.NodeId);
                }
            }

            // Also check references on this node for inverse AlarmGroupMember
            var references = new List<IReference>();
            GetReferences(context, references);

            foreach (IReference reference in references)
            {
                if (reference.ReferenceTypeId == ReferenceTypeIds.AlarmGroupMember &&
                    reference.IsInverse)
                {
                    var targetId = ExpandedNodeId.ToNodeId(
                        reference.TargetId, context.NamespaceUris);
                    if (!targetId.IsNull)
                    {
                        groupList.Add(targetId);
                    }
                }
            }

            groups = groupList.ToArray();
            return ServiceResult.Good;
        }

        /// <summary>
        /// Applies a comment if it is not null/empty.
        /// </summary>
        private void ApplyComment(ISystemContext context, LocalizedText comment)
        {
            if (!comment.IsNullOrEmpty)
            {
                string userId = ClientUserId?.Value ?? string.Empty;
                SetComment(context, comment, userId);
            }
        }

        /// <summary>
        /// Common pattern for Part 9 alarm method handlers: validate, execute,
        /// report state change, emit audit event.
        /// </summary>
        private ServiceResult HandleAlarmMethod(
            ISystemContext context,
            MethodState method,
            LocalizedText comment,
            string auditInfoKey,
            string auditMessage,
            string sourceName,
            Func<ServiceResult> validate,
            Func<ISystemContext, LocalizedText, ServiceResult> execute,
            Func<AuditConditionEventState> createAuditEvent)
        {
            ServiceResult? error = null;

            try
            {
                if (!EnabledState!.Id!.Value)
                {
                    return error = StatusCodes.BadConditionDisabled;
                }

                error = validate();
                if (ServiceResult.IsBad(error))
                {
                    return error;
                }

                error = execute(context, comment);

                if (ServiceResult.IsGood(error))
                {
                    ReportStateChange(context, false);
                }
            }
            finally
            {
                if (AreEventsMonitored)
                {
                    AuditConditionEventState e = createAuditEvent();

                    var info = new TranslationInfo(auditInfoKey, "en-US", auditMessage);

                    e.Initialize(
                        context,
                        this,
                        EventSeverity.Low,
                        new LocalizedText(info),
                        ServiceResult.IsGood(error),
                        DateTime.UtcNow);

                    e.SetChildValue(context, BrowseNames.SourceNode, NodeId, false);
                    e.SetChildValue(context, BrowseNames.SourceName, sourceName, false);
                    e.SetChildValue(context, BrowseNames.MethodId, method.NodeId, false);

                    ReportEvent(context, e);
                }
            }

            return error;
        }

        /// <summary>
        /// Processes a re-alarm occurrence. Call this from an external timer
        /// when <see cref="ReAlarmTime"/> is configured and the alarm is
        /// active and unacknowledged.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <remarks>
        /// <para>
        /// Re-alarming re-reports the alarm event, sets AckedState to false,
        /// clears SilenceState, and increments ReAlarmRepeatCount.
        /// </para>
        /// <para>
        /// The timer itself is not managed by the state type — this is an
        /// opt-in helper for applications that implement re-alarm policy.
        /// </para>
        /// </remarks>
        public virtual void ProcessReAlarm(ISystemContext context)
        {
            if (!EnabledState!.Id!.Value || !ActiveState!.Id!.Value)
            {
                return;
            }

            // Reset acknowledged state
            SetAcknowledgedState(context, false);

            // Clear silence — re-alarm makes the alarm audible again
            if (SilenceState is { } silenceState && silenceState.Id!.Value)
            {
                SetSilenceState(context, false);
            }

            // Increment repeat count
            if (ReAlarmRepeatCount != null)
            {
                ReAlarmRepeatCount.Value++;
                ReAlarmRepeatCount.Timestamp = DateTimeUtc.Now;
            }

            ReportStateChange(context, false);
            ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Resets the re-alarm repeat count. Call when the alarm deactivates
        /// or is acknowledged.
        /// </summary>
        /// <param name="context">The system context.</param>
        public void ResetReAlarmRepeatCount(ISystemContext context)
        {
            if (ReAlarmRepeatCount != null && ReAlarmRepeatCount.Value != 0)
            {
                ReAlarmRepeatCount.Value = 0;
                ReAlarmRepeatCount.Timestamp = DateTimeUtc.Now;
                ClearChangeMasks(context, includeChildren: true);
            }
        }

        /// <summary>
        /// Gets whether this alarm is configured for re-alarming.
        /// </summary>
        public bool IsReAlarmEnabled =>
            ReAlarmTime is { } reAlarmTime && reAlarmTime.Value > 0;

        /// <summary>
        /// Updates the audible state of the alarm when it activates.
        /// Call this after <see cref="SetActiveState"/> when
        /// <see cref="AudibleEnabled"/> is configured.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">Whether the alarm is becoming active.</param>
        /// <param name="soundData">
        /// The audio data to set on <see cref="AudibleSound"/> when
        /// the alarm activates and is audible. May be default to
        /// leave the sound unchanged.
        /// </param>
        public virtual void UpdateAudibleState(
            ISystemContext context,
            bool active,
            ByteString soundData = default)
        {
            if (AudibleEnabled == null || !AudibleEnabled.Value)
            {
                return;
            }

            if (active)
            {
                // New activation: set sound and clear silence
                if (AudibleSound != null && !soundData.IsNull)
                {
                    AudibleSound.Value = soundData;
                    AudibleSound.Timestamp = DateTimeUtc.Now;
                }

                // Activating an audible alarm clears the silence state
                if (SilenceState is { } silenceState && silenceState.Id!.Value)
                {
                    SetSilenceState(context, false);
                }
            }

            ClearChangeMasks(context, includeChildren: true);
        }

    }

    /// <summary>
    /// Delegate for simple alarm condition events (Silence, Reset).
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="alarm">The alarm that raised the event.</param>
    /// <returns>Good to allow the operation; a Bad status code to veto it.</returns>
    public delegate ServiceResult AlarmConditionSimpleEventHandler(
        ISystemContext context,
        AlarmConditionState alarm);

    /// <summary>
    /// Delegate for suppress/unsuppress events.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="alarm">The alarm that raised the event.</param>
    /// <param name="suppressing">
    /// <c>true</c> when the alarm is being suppressed;
    /// <c>false</c> when it is being unsuppressed.
    /// </param>
    /// <returns>Good to allow the operation; a Bad status code to veto it.</returns>
    public delegate ServiceResult AlarmConditionSuppressEventHandler(
        ISystemContext context,
        AlarmConditionState alarm,
        bool suppressing);

    /// <summary>
    /// Delegate for out-of-service state change events.
    /// </summary>
    /// <param name="context">The current system context.</param>
    /// <param name="alarm">The alarm that raised the event.</param>
    /// <param name="outOfService">
    /// <c>true</c> when the alarm is being removed from service;
    /// <c>false</c> when it is being placed in service.
    /// </param>
    /// <returns>Good to allow the operation; a Bad status code to veto it.</returns>
    public delegate ServiceResult AlarmConditionOutOfServiceEventHandler(
        ISystemContext context,
        AlarmConditionState alarm,
        bool outOfService);
}
