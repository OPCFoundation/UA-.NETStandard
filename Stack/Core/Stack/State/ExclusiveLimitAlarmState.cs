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
    public partial class ExclusiveLimitAlarmState
    {
        #region Public Methods
        /// <summary>
        /// Sets the active state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">if set to <c>true</c> the condition is active.</param>
        public override void SetActiveState(
            ISystemContext context,
            bool active)
        {
            // set it inactive.
            if (!active)
            {
                SetLimitState(context, LimitAlarmStates.Inactive);
                return;
            }

            // check if the level state machine needs an initial state.
            if (this.LimitState.CurrentState.Id.Value != null)
            {
                base.SetActiveState(context, true);
                return;
            }

            // assume a high if the high limit is specified.
            if (this.HighLimit != null)
            {
                SetLimitState(context, LimitAlarmStates.High);
            }
            else
            {
                SetLimitState(context, LimitAlarmStates.Low);
            }
        }

        /// <summary>
        /// Sets the limit state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="limit">The bit masks specifying the current state.</param>
        public virtual void SetLimitState(
            ISystemContext context,
            LimitAlarmStates limit)
        {
            switch (limit)
            {
                case LimitAlarmStates.HighHigh:
                {
                    this.LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_HighHigh);
                    break;
                }

                case LimitAlarmStates.High:
                {
                    this.LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_High);
                    break;
                }

                case LimitAlarmStates.Low:
                {
                    this.LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_Low);
                    break;
                }

                case LimitAlarmStates.LowLow:
                {
                    this.LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_LowLow);
                    break;
                }

                default:
                {
                    this.LimitState.SetState(context, 0);
                    break;
                }
           }

           SetActiveEffectiveSubState(context, this.LimitState.CurrentState.Value, DateTime.UtcNow);
           base.SetActiveState(context, limit != LimitAlarmStates.Inactive);
        }
        #endregion
    }
}
