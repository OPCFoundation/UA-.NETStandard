/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Xml;
using System.Text;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;

namespace Opc.Ua
{
    public partial class NonExclusiveLimitAlarmState
    {
        #region Public Methods
        /// <summary>
        /// Sets the limit state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="limit">The bit masks specifying the current state.</param>
        public virtual void SetLimitState(
            ISystemContext context,
            LimitAlarmStates limit)
        {
            if (this.HighState != null)
            {
                UpdateState(this.HighState, ((limit & LimitAlarmStates.High) != 0 || (limit & LimitAlarmStates.HighHigh) != 0));
            }

            if (this.HighHighState != null)
            {
                UpdateState(this.HighHighState, (limit & LimitAlarmStates.HighHigh) != 0);
            }

            if (this.LowState != null)
            {
                UpdateState(this.LowState, ((limit & LimitAlarmStates.Low) != 0 || (limit & LimitAlarmStates.LowLow) != 0));
            }

            if (this.LowLowState != null)
            {
                UpdateState(this.LowLowState, (limit & LimitAlarmStates.LowLow) != 0);
            }

            // select an appropriate effective display name for the active state.
            TranslationInfo displayName = null;

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
        #endregion

        #region Private Methods
        /// <summary>
        /// Updates the state.
        /// </summary>
        /// <param name="limit">The limit.</param>
        /// <param name="active">if set to <c>true</c> is the state is active.</param>
        private void UpdateState(TwoStateVariableState limit, bool active)
        {
            TranslationInfo state = null;

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

            if (limit.TransitionTime != null)
            {
                limit.TransitionTime.Value = DateTime.UtcNow;
            }
        }
        #endregion
    }
}
