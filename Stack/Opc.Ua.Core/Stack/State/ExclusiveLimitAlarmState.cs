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

namespace Opc.Ua
{
    public partial class ExclusiveLimitAlarmState
    {
        /// <summary>
        /// Sets the active state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">if set to <c>true</c> the condition is active.</param>
        public override void SetActiveState(ISystemContext context, bool active)
        {
            // set it inactive.
            if (!active)
            {
                SetLimitState(context, LimitAlarmStates.Inactive);
                return;
            }

            // check if the level state machine needs an initial state.
            if (LimitState.CurrentState.Id.Value != null)
            {
                base.SetActiveState(context, true);
                return;
            }

            // assume a high if the high limit is specified.
            if (HighLimit != null)
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
        public virtual void SetLimitState(ISystemContext context, LimitAlarmStates limit)
        {
            switch (limit)
            {
                case LimitAlarmStates.HighHigh:
                    LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_HighHigh);
                    break;
                case LimitAlarmStates.High:
                    LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_High);
                    break;
                case LimitAlarmStates.Low:
                    LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_Low);
                    break;
                case LimitAlarmStates.LowLow:
                    LimitState.SetState(context, Objects.ExclusiveLimitStateMachineType_LowLow);
                    break;
                case LimitAlarmStates.Inactive:
                    LimitState.SetState(context, 0);
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        $"Invalid limit state {limit} specified.");
            }

            SetActiveEffectiveSubState(context, LimitState.CurrentState.Value, DateTime.UtcNow);
            base.SetActiveState(context, limit != LimitAlarmStates.Inactive);
        }
    }
}
