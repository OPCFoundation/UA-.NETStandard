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
using System.Threading;
using System.Threading.Tasks;

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

            ExclusiveLimitStateMachineState limitState = LimitState!;

            // check if the level state machine needs an initial state.
            if (!limitState.CurrentState!.Id!.Value.IsNull)
            {
                base.SetActiveState(context, true, clearChangeMasks: true, updateRetainState: true);
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
        /// Sets the active state and awaits state-change notification sinks.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">Whether the condition is active.</param>
        /// <param name="cancellationToken">
        /// The token is checked before mutation; cancellation is not observed after mutation starts.
        /// </param>
        /// <remarks>
        /// Derived classes that customize <see cref="SetActiveState(ISystemContext, bool)"/> must also override
        /// this method.
        /// </remarks>
        public override async ValueTask SetActiveStateAsync(
            ISystemContext context,
            bool active,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!active)
            {
                await SetLimitStateAsync(context, LimitAlarmStates.Inactive, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            ExclusiveLimitStateMachineState limitState = LimitState!;
            if (!limitState.CurrentState!.Id!.Value.IsNull)
            {
                await base.SetActiveStateAsync(
                    context, true, clearChangeMasks: true, updateRetainState: true, cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            await SetLimitStateAsync(
                context,
                HighLimit != null ? LimitAlarmStates.High : LimitAlarmStates.Low,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the active and limit states and optionally publishes their change masks.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">Whether the condition is active.</param>
        /// <param name="clearChangeMasks">Whether to notify state observers now.</param>
        /// <param name="updateRetainState">Whether to derive the Retain state.</param>
        /// <remarks>
        /// Calls with both options enabled dispatch through the existing two-argument override.
        /// Derived classes customizing deferred updates must override this overload.
        /// </remarks>
        public override void SetActiveState(
            ISystemContext context, bool active, bool clearChangeMasks, bool updateRetainState)
        {
            ValidateActiveStateUpdate(active, clearChangeMasks);
            if (clearChangeMasks && updateRetainState)
            {
                SetActiveState(context, active);
                return;
            }

            UpdateLimitStateForActivation(context, active);
            base.SetActiveState(context, active, clearChangeMasks, updateRetainState);
        }

        /// <summary>
        /// Sets the active and limit states and optionally awaits state-change notification sinks.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">Whether the condition is active.</param>
        /// <param name="clearChangeMasks">Whether to notify state observers now.</param>
        /// <param name="updateRetainState">Whether to derive the Retain state.</param>
        /// <param name="cancellationToken">
        /// The token is checked before mutation; cancellation is not observed after mutation starts.
        /// </param>
        /// <remarks>
        /// Calls with both options enabled dispatch through the existing asynchronous override.
        /// Derived classes customizing deferred updates must override this overload.
        /// </remarks>
        public override async ValueTask SetActiveStateAsync(
            ISystemContext context,
            bool active,
            bool clearChangeMasks,
            bool updateRetainState,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateActiveStateUpdate(active, clearChangeMasks);
            if (clearChangeMasks && updateRetainState)
            {
                await SetActiveStateAsync(context, active, cancellationToken).ConfigureAwait(false);
                return;
            }

            UpdateLimitStateForActivation(context, active);
            await base.SetActiveStateAsync(
                context, active, clearChangeMasks, updateRetainState, CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Sets the limit state of the condition.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="limit">The bit masks specifying the current state.</param>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void SetLimitState(ISystemContext context, LimitAlarmStates limit)
        {
            UpdateLimitState(context, limit);
            base.SetActiveState(
                context, limit != LimitAlarmStates.Inactive, clearChangeMasks: true, updateRetainState: true);
        }

        /// <summary>
        /// Sets the limit state and awaits state-change notification sinks.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="limit">The bit masks specifying the current state.</param>
        /// <param name="cancellationToken">
        /// The token is checked before mutation; cancellation is not observed after mutation starts.
        /// </param>
        /// <remarks>
        /// Derived classes that customize <see cref="SetLimitState"/> must also override this method.
        /// </remarks>
        public virtual async ValueTask SetLimitStateAsync(
            ISystemContext context,
            LimitAlarmStates limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UpdateLimitState(context, limit);
            await base.SetActiveStateAsync(
                context, limit != LimitAlarmStates.Inactive, clearChangeMasks: true, updateRetainState: true,
                CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Initializes or clears the limit state before a deferred active-state update.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="active">Whether the condition is active.</param>
        private void UpdateLimitStateForActivation(ISystemContext context, bool active)
        {
            if (!active)
            {
                UpdateLimitState(context, LimitAlarmStates.Inactive);
            }
            else if (LimitState!.CurrentState!.Id!.Value.IsNull)
            {
                UpdateLimitState(context, HighLimit != null ? LimitAlarmStates.High : LimitAlarmStates.Low);
            }
        }

        /// <summary>
        /// Updates limit attributes without dispatching state-change notifications.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="limit">The requested limit state.</param>
        private void UpdateLimitState(ISystemContext context, LimitAlarmStates limit)
        {
            ExclusiveLimitStateMachineState limitState = LimitState!;
            switch (limit)
            {
                case LimitAlarmStates.HighHigh:
                    limitState.SetState(context, Objects.ExclusiveLimitStateMachineType_HighHigh);
                    break;
                case LimitAlarmStates.High:
                    limitState.SetState(context, Objects.ExclusiveLimitStateMachineType_High);
                    break;
                case LimitAlarmStates.Low:
                    limitState.SetState(context, Objects.ExclusiveLimitStateMachineType_Low);
                    break;
                case LimitAlarmStates.LowLow:
                    limitState.SetState(context, Objects.ExclusiveLimitStateMachineType_LowLow);
                    break;
                case LimitAlarmStates.Inactive:
                    limitState.SetState(context, 0);
                    break;
                default:
                    throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                        $"Invalid limit state {limit} specified.");
            }

            SetActiveEffectiveSubState(context, limitState.CurrentState!.Value, DateTime.UtcNow);
        }
    }
}
