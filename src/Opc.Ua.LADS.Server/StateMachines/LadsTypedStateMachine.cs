/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

namespace Opc.Ua.LADS.Server.StateMachines
{
    /// <summary>
    /// A <see cref="LadsStateMachine"/> addressed by the values of a state enumeration.
    /// </summary>
    /// <typeparam name="TEnum">The state enumeration.</typeparam>
    internal sealed class LadsStateMachine<TEnum>
        where TEnum : struct, Enum
    {
        /// <summary>
        /// Creates the driver and writes the initial state.
        /// </summary>
        /// <param name="stateMachine">The state machine instance.</param>
        /// <param name="context">The system context.</param>
        /// <param name="map">The enumeration map.</param>
        /// <param name="initial">The initial state.</param>
        public LadsStateMachine(
            FiniteStateMachineState stateMachine,
            ISystemContext context,
            LadsStateMap<TEnum> map,
            TEnum initial)
        {
            m_map = map;
            Driver = new LadsStateMachine(stateMachine, context, map.Table);
            Driver.StateChanged += id => StateChanged?.Invoke(m_map.ToValue(id));
            Driver.SetInitialState(map.ToId(initial));
        }

        /// <summary>
        /// Gets the untyped driver.
        /// </summary>
        public LadsStateMachine Driver { get; }

        /// <summary>
        /// Gets the current state.
        /// </summary>
        public TEnum Current => m_map.ToValue(Driver.CurrentStateId);

        /// <summary>
        /// Raised after the state changed.
        /// </summary>
        public event Action<TEnum>? StateChanged;

        /// <summary>
        /// Writes a state without a transition.
        /// </summary>
        /// <param name="state">The state.</param>
        public void SetState(TEnum state)
        {
            Driver.SetInitialState(m_map.ToId(state));
        }

        /// <summary>
        /// Returns whether a transition leads from the current state to the target.
        /// </summary>
        /// <param name="target">The target state.</param>
        /// <returns><see langword="true"/> when the transition exists.</returns>
        public bool CanTransitionTo(TEnum target)
        {
            return Driver.CanTransitionTo(m_map.ToId(target));
        }

        /// <summary>
        /// Applies the transition to a state.
        /// </summary>
        /// <param name="target">The target state.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns><see langword="false"/> when a guard vetoed the transition.</returns>
        public ValueTask<bool> TransitionToAsync(TEnum target, CancellationToken cancellationToken = default)
        {
            return Driver.TransitionToAsync(m_map.ToId(target), cancellationToken);
        }

        /// <summary>
        /// Applies the transition to a state synchronously.
        /// </summary>
        /// <param name="target">The target state.</param>
        /// <returns><see langword="false"/> when a guard vetoed the transition.</returns>
        public bool TransitionTo(TEnum target)
        {
            return Driver.TransitionTo(m_map.ToId(target));
        }

        /// <summary>
        /// Applies a sequence of transitions.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <param name="targets">The states to visit in order.</param>
        /// <returns><see langword="false"/> when a guard vetoed one of the transitions.</returns>
        public async ValueTask<bool> TransitionThroughAsync(CancellationToken cancellationToken, params TEnum[] targets)
        {
            foreach (TEnum target in targets)
            {
                if (!await TransitionToAsync(target, cancellationToken).ConfigureAwait(false))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Throws <see cref="StatusCodes.BadInvalidState"/> unless the current state is one of the expected ones.
        /// </summary>
        /// <param name="expected">The accepted states.</param>
        public void Require(params TEnum[] expected)
        {
            TEnum current = Current;
            foreach (TEnum state in expected)
            {
                if (current.Equals(state))
                {
                    return;
                }
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "The state machine '{0}' is in state '{1}'.",
                Driver.StateMachine.BrowseName,
                current);
        }

        private readonly LadsStateMap<TEnum> m_map;
    }
}
