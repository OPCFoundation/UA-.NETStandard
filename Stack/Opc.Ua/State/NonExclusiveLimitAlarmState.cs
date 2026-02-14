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
            limit.Id.Value = active;

            limit.TransitionTime?.Value = DateTime.UtcNow;
        }
    }
}
