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

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// The states of the OPC 30500-1 <c>LADSDeviceStateMachineType</c>.
    /// </summary>
    public enum LadsDeviceStateValue
    {
        /// <summary>
        /// The device initializes and is not yet operational.
        /// </summary>
        Initialization,

        /// <summary>
        /// The device is operational.
        /// </summary>
        Operate,

        /// <summary>
        /// The device sleeps and can return to <see cref="Operate"/>.
        /// </summary>
        Sleep,

        /// <summary>
        /// The device shut down. It cannot leave this state.
        /// </summary>
        Shutdown
    }

    /// <summary>
    /// The states of the OPC 30500-1 <c>FunctionalStateMachineType</c> shared by functional units and control
    /// functions.
    /// </summary>
    public enum LadsFunctionalStateValue
    {
        /// <summary>
        /// The unit or function is stopped.
        /// </summary>
        Stopped,

        /// <summary>
        /// The unit or function runs.
        /// </summary>
        Running,

        /// <summary>
        /// The unit or function stops.
        /// </summary>
        Stopping,

        /// <summary>
        /// The unit or function aborts.
        /// </summary>
        Aborting,

        /// <summary>
        /// The unit or function was aborted and must be cleared.
        /// </summary>
        Aborted,

        /// <summary>
        /// The unit or function clears after an abort.
        /// </summary>
        Clearing
    }

    /// <summary>
    /// The states of the OPC 30500-1 <c>RunningStateMachineType</c>, which refines the <c>Running</c> state
    /// of a functional unit that executes a program.
    /// </summary>
    public enum LadsRunningStateValue
    {
        /// <summary>
        /// No program executes.
        /// </summary>
        Idle,

        /// <summary>
        /// A program starts.
        /// </summary>
        Starting,

        /// <summary>
        /// A program executes.
        /// </summary>
        Execute,

        /// <summary>
        /// A program completes.
        /// </summary>
        Completing,

        /// <summary>
        /// A program completed.
        /// </summary>
        Complete,

        /// <summary>
        /// The state machine returns to <see cref="Idle"/>.
        /// </summary>
        Resetting,

        /// <summary>
        /// A program moves to <see cref="Held"/>.
        /// </summary>
        Holding,

        /// <summary>
        /// A program is held, typically by the operator.
        /// </summary>
        Held,

        /// <summary>
        /// A held program resumes.
        /// </summary>
        Unholding,

        /// <summary>
        /// A program moves to <see cref="Suspended"/>.
        /// </summary>
        Suspending,

        /// <summary>
        /// A program is suspended, typically by an upstream or downstream condition.
        /// </summary>
        Suspended,

        /// <summary>
        /// A suspended program resumes.
        /// </summary>
        Unsuspending
    }

    /// <summary>
    /// The states of the OPC 30500-1 <c>CoverStateMachineType</c>.
    /// </summary>
    public enum LadsCoverStateValue
    {
        /// <summary>
        /// The cover is open.
        /// </summary>
        Opened,

        /// <summary>
        /// The cover closes.
        /// </summary>
        Closing,

        /// <summary>
        /// The cover is closed.
        /// </summary>
        Closed,

        /// <summary>
        /// The cover locks.
        /// </summary>
        Locking,

        /// <summary>
        /// The cover is closed and locked.
        /// </summary>
        Locked,

        /// <summary>
        /// The cover unlocks.
        /// </summary>
        Unlocking,

        /// <summary>
        /// The cover opens.
        /// </summary>
        Opening,

        /// <summary>
        /// The cover failed. <c>Reset</c> opens it again.
        /// </summary>
        Error
    }

    /// <summary>
    /// The states of the OPC 30500-2 <c>MaintenanceEventStateMachineType</c> that a LADS maintenance task uses.
    /// </summary>
    public enum LadsMaintenanceStateValue
    {
        /// <summary>
        /// The maintenance is planned.
        /// </summary>
        Planned,

        /// <summary>
        /// The maintenance executes.
        /// </summary>
        Executing,

        /// <summary>
        /// The maintenance finished.
        /// </summary>
        Finished
    }
}
