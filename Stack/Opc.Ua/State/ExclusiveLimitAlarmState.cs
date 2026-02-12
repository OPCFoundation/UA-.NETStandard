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
            if (!LimitState.CurrentState.Id.Value.IsNull)
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
        /// <exception cref="ServiceResultException"></exception>
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
