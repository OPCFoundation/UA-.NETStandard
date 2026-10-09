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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Machinery.Server.StateMachines
{
    /// <summary>
    /// Drives the OPC 40001-1 <c>MachineryItemState</c> state machine of one
    /// machinery item.
    /// </summary>
    /// <remarks>
    /// OPC 40001-1 declares no cause methods on either of its state machines,
    /// so a client can never request a transition. The server owns the state
    /// and pushes it here; clients observe it through
    /// <c>CurrentState</c> — either by reading it or by streaming
    /// <c>GetCurrentFiniteStateAsync</c> / <c>ObserveFiniteTransitionsAsync</c>
    /// off the generated <c>MachineryItemState_StateMachineTypeClient</c>.
    /// </remarks>
    public interface IMachineryItemStateController
    {
        /// <summary>
        /// Gets the state machine's NodeId.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets the state the machine is currently in.
        /// </summary>
        MachineryItemStateValue CurrentState { get; }

        /// <summary>
        /// Moves the machine into <paramref name="state"/>, writing
        /// <c>CurrentState</c> and <c>LastTransition</c> and notifying
        /// subscribers. A move to the state the machine is already in is the
        /// self-transition the model declares, not a no-op.
        /// </summary>
        /// <param name="state">The state to move into.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// <see langword="true"/> when the transition was applied;
        /// <see langword="false"/> when a registered pre-transition guard
        /// vetoed it.
        /// </returns>
        ValueTask<bool> SetStateAsync(
            MachineryItemStateValue state,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Drives the OPC 40001-1 <c>MachineryOperationMode</c> state machine of
    /// one machinery item. See <see cref="IMachineryItemStateController"/> for
    /// why this is server-driven.
    /// </summary>
    public interface IMachineryOperationModeController
    {
        /// <summary>
        /// Gets the state machine's NodeId.
        /// </summary>
        NodeId NodeId { get; }

        /// <summary>
        /// Gets the operation mode the machine is currently in.
        /// </summary>
        MachineryOperationModeValue CurrentMode { get; }

        /// <summary>
        /// Moves the machine into <paramref name="mode"/>.
        /// </summary>
        /// <param name="mode">The operation mode to move into.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>
        /// <see langword="true"/> when the transition was applied;
        /// <see langword="false"/> when a registered pre-transition guard
        /// vetoed it.
        /// </returns>
        ValueTask<bool> SetModeAsync(
            MachineryOperationModeValue mode,
            CancellationToken cancellationToken = default);
    }
}
