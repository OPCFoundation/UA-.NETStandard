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
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The commands a client or the application issues to a LADS function.
    /// </summary>
    public enum LadsFunctionCommandKind
    {
        /// <summary>
        /// Starts a control function with its current target value.
        /// </summary>
        Start,

        /// <summary>
        /// Starts a control function with a new target value.
        /// </summary>
        StartWithTargetValue,

        /// <summary>
        /// Stops a control function.
        /// </summary>
        Stop,

        /// <summary>
        /// Aborts a control function.
        /// </summary>
        Abort,

        /// <summary>
        /// Clears an aborted control function.
        /// </summary>
        Clear,

        /// <summary>
        /// Writes a new target value of a control function.
        /// </summary>
        SetTargetValue,

        /// <summary>
        /// Modifies the target value of a relative analog control function.
        /// </summary>
        ModifyTargetValueBy,

        /// <summary>
        /// Resets the totalized value of a totalizer analog control function.
        /// </summary>
        ResetTotalizer,

        /// <summary>
        /// Selects the controller mode of a multi-mode analog control function.
        /// </summary>
        SetMode,

        /// <summary>
        /// Opens a cover.
        /// </summary>
        Open,

        /// <summary>
        /// Closes a cover.
        /// </summary>
        Close,

        /// <summary>
        /// Locks a cover.
        /// </summary>
        Lock,

        /// <summary>
        /// Unlocks a cover.
        /// </summary>
        Unlock,

        /// <summary>
        /// Resets a cover in the error state.
        /// </summary>
        Reset
    }

    /// <summary>
    /// The maintenance commands of a LADS maintenance task.
    /// </summary>
    public enum LadsMaintenanceCommand
    {
        /// <summary>
        /// Starts the maintenance task.
        /// </summary>
        Start,

        /// <summary>
        /// Stops the executing maintenance task.
        /// </summary>
        Stop,

        /// <summary>
        /// Resets a finished maintenance task to planned.
        /// </summary>
        Reset
    }

    /// <summary>
    /// A command issued to a LADS function, passed to the
    /// <see cref="ILadsFunctionProvider"/> before the runtime applies it.
    /// </summary>
    public sealed class LadsFunctionCommand
    {
        /// <summary>
        /// Creates a command.
        /// </summary>
        /// <param name="function">The function the command is issued to.</param>
        /// <param name="kind">The kind of the command.</param>
        /// <param name="value">The argument of the command, if it has one.</param>
        /// <exception cref="ArgumentNullException"><paramref name="function"/> is <c>null</c>.</exception>
        public LadsFunctionCommand(ILadsFunction function, LadsFunctionCommandKind kind, Variant value = default)
        {
            Function = function ?? throw new ArgumentNullException(nameof(function));
            Kind = kind;
            Value = value;
        }

        /// <summary>
        /// The function the command is issued to.
        /// </summary>
        public ILadsFunction Function { get; }

        /// <summary>
        /// The kind of the command.
        /// </summary>
        public LadsFunctionCommandKind Kind { get; }

        /// <summary>
        /// The argument of the command: the target value, the modification or the mode index.
        /// </summary>
        public Variant Value { get; }
    }

    /// <summary>
    /// Connects LADS devices to the hardware: decides on device state, operation mode and
    /// maintenance task changes before the runtime applies them.
    /// </summary>
    public interface ILadsDeviceProvider
    {
        /// <summary>
        /// Called before the device state machine moves to <paramref name="target"/>. A bad result
        /// rejects the change.
        /// </summary>
        /// <param name="device">The device.</param>
        /// <param name="target">The requested state.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the hardware operation.</returns>
        ValueTask<ServiceResult> OnDeviceStateChangingAsync(
            ILadsDevice device,
            LadsDeviceStateValue target,
            CancellationToken cancellationToken);

        /// <summary>
        /// Called before the operation mode moves to <paramref name="target"/>. A bad result
        /// rejects the change.
        /// </summary>
        /// <param name="device">The device.</param>
        /// <param name="target">The requested operation mode.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the hardware operation.</returns>
        ValueTask<ServiceResult> OnOperationModeChangingAsync(
            ILadsDevice device,
            MachineryOperationModeValue target,
            CancellationToken cancellationToken);

        /// <summary>
        /// Called before a maintenance task applies a command. A bad result rejects the command.
        /// </summary>
        /// <param name="task">The maintenance task.</param>
        /// <param name="command">The command.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the hardware operation.</returns>
        ValueTask<ServiceResult> OnMaintenanceTaskAsync(
            ILadsMaintenanceTask task,
            LadsMaintenanceCommand command,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Connects LADS functions to the hardware: applies the commands of control functions and covers
    /// before the runtime moves their state machines.
    /// </summary>
    public interface ILadsFunctionProvider
    {
        /// <summary>
        /// Called before the runtime applies a command. A bad result rejects the command and leaves
        /// the function unchanged.
        /// </summary>
        /// <param name="command">The command.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The result of the hardware operation.</returns>
        ValueTask<ServiceResult> OnCommandAsync(LadsFunctionCommand command, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Executes the program runs of a functional unit.
    /// </summary>
    public interface ILadsProgramExecutor
    {
        /// <summary>
        /// Executes a program run. The run completes when the returned task completes; it is
        /// stopped or aborted when <paramref name="cancellationToken"/> is cancelled, and it is
        /// aborted when the task fails.
        /// </summary>
        /// <param name="run">The program run.</param>
        /// <param name="cancellationToken">Cancelled when the run is stopped or aborted.</param>
        /// <returns>A task that completes when the run has finished.</returns>
        ValueTask ExecuteAsync(LadsProgramRun run, CancellationToken cancellationToken);
    }
}
