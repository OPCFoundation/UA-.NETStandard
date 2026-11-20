// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    public partial class NonExclusiveLimitAlarmState
    {
        /// <summary>
        /// Sets the limit state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="limit">The bit masks specifying the current state.</param>
        public virtual void SetLimitState(ISystemContext context, LimitAlarmStates limit)
        {
            if (HighState != null)
            {
                UpdateState(
                    HighState,
                    (limit & LimitAlarmStates.High) != 0 ||
                    (limit & LimitAlarmStates.HighHigh) != 0);
            }

            if (HighHighState != null)
            {
                UpdateState(HighHighState, (limit & LimitAlarmStates.HighHigh) != 0);
            }

            if (LowState != null)
            {
                UpdateState(
                    LowState,
                    (limit & LimitAlarmStates.Low) != 0 || (limit & LimitAlarmStates.LowLow) != 0);
            }

            if (LowLowState != null)
            {
                UpdateState(LowLowState, (limit & LimitAlarmStates.LowLow) != 0);
            }

            // select an appropriate effective display name for the active state.
            TranslationInfo displayName;
            if ((limit & LimitAlarmStates.HighHigh) != 0)
            {
                displayName = new TranslationInfo(
                    "ConditionStateHighHighActive",
                    "en-US",
                    ConditionStateNames.HighHighActive);
            }
            else if ((limit & LimitAlarmStates.LowLow) != 0)
            {
                displayName = new TranslationInfo(
                    "ConditionStateLowLowActive",
                    "en-US",
                    ConditionStateNames.LowLowActive);
            }
            else if ((limit & LimitAlarmStates.High) != 0)
            {
                displayName = new TranslationInfo(
                    "ConditionStateHighActive",
                    "en-US",
                    ConditionStateNames.HighActive);
            }
            else if ((limit & LimitAlarmStates.Low) != 0)
            {
                displayName = new TranslationInfo(
                    "ConditionStateLowActive",
                    "en-US",
                    ConditionStateNames.LowActive);
            }
            else
            {
                displayName = new TranslationInfo(
                    "ConditionStateInactive",
                    "en-US",
                    ConditionStateNames.Inactive);
            }

            // update the active superstae.
            SetActiveEffectiveSubState(context, new LocalizedText(displayName), DateTime.UtcNow);
            UpdateEffectiveState(context);
        }

        /// <summary>
        /// Updates the state.
        /// </summary>
        /// <param name="limit">The limit.</param>
        /// <param name="active">if set to <c>true</c> is the state is active.</param>
        private static void UpdateState(TwoStateVariableState limit, bool active)
        {
            TranslationInfo state;
            if (active)
            {
                state = new TranslationInfo(
                    "ConditionStateActive",
                    "en-US",
                    ConditionStateNames.Active);
            }
            else
            {
                state = new TranslationInfo(
                    "ConditionStateInactive",
                    "en-US",
                    ConditionStateNames.Inactive);
            }

            limit.Value = new LocalizedText(state);
            limit.Id!.Value = active; // Id property is initialized as part of the LimitAlarm state machine construction

            limit.TransitionTime?.Value = DateTime.UtcNow;
        }
    }
}
