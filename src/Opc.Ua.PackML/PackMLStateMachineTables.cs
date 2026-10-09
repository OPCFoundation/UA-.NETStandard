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

namespace Opc.Ua.PackML
{
    /// <summary>
    /// The PackML state numbers of ISA-TR88.00.02, as the OPC 30050
    /// NodeSet publishes them in each state's <c>StateNumber</c> Property.
    /// </summary>
    public static class PackMLStateNumbers
    {
        /// <summary>Clearing (MachineState).</summary>
        public const uint Clearing = 1;

        /// <summary>Stopped (MachineState).</summary>
        public const uint Stopped = 2;

        /// <summary>Starting (ExecuteState).</summary>
        public const uint Starting = 3;

        /// <summary>Idle (ExecuteState).</summary>
        public const uint Idle = 4;

        /// <summary>Suspended (ExecuteState).</summary>
        public const uint Suspended = 5;

        /// <summary>Execute (ExecuteState).</summary>
        public const uint Execute = 6;

        /// <summary>Stopping (MachineState).</summary>
        public const uint Stopping = 7;

        /// <summary>Aborting (base state machine).</summary>
        public const uint Aborting = 8;

        /// <summary>Aborted (base state machine).</summary>
        public const uint Aborted = 9;

        /// <summary>Holding (ExecuteState).</summary>
        public const uint Holding = 10;

        /// <summary>Held (ExecuteState).</summary>
        public const uint Held = 11;

        /// <summary>Unholding (ExecuteState).</summary>
        public const uint Unholding = 12;

        /// <summary>Suspending (ExecuteState).</summary>
        public const uint Suspending = 13;

        /// <summary>Unsuspending (ExecuteState).</summary>
        public const uint Unsuspending = 14;

        /// <summary>Resetting (ExecuteState).</summary>
        public const uint Resetting = 15;

        /// <summary>Completing (ExecuteState).</summary>
        public const uint Completing = 16;

        /// <summary>Complete (ExecuteState).</summary>
        public const uint Complete = 17;

        /// <summary>Running (MachineState).</summary>
        public const uint Running = 18;

        /// <summary>Cleared (base state machine).</summary>
        public const uint Cleared = 19;

        /// <summary>
        /// Maps the numeric id of a PackML state object (the identifier of
        /// <c>CurrentState/Id</c> in the PackML namespace) onto its state
        /// number.
        /// </summary>
        /// <param name="stateId">The state object's numeric id.</param>
        /// <returns>The state number, or 0 for an unknown id.</returns>
        public static uint FromStateId(uint stateId)
        {
            return stateId switch
            {
                Objects.PackMLBaseStateMachineType_Aborting => PackMLStateNumbers.Aborting,
                Objects.PackMLBaseStateMachineType_Aborted => PackMLStateNumbers.Aborted,
                Objects.PackMLBaseStateMachineType_Cleared => PackMLStateNumbers.Cleared,
                Objects.PackMLMachineStateMachineType_Clearing => PackMLStateNumbers.Clearing,
                Objects.PackMLMachineStateMachineType_Stopped => PackMLStateNumbers.Stopped,
                Objects.PackMLMachineStateMachineType_Running => PackMLStateNumbers.Running,
                Objects.PackMLMachineStateMachineType_Stopping => PackMLStateNumbers.Stopping,
                Objects.PackMLExecuteStateMachineType_Resetting => PackMLStateNumbers.Resetting,
                Objects.PackMLExecuteStateMachineType_Idle => PackMLStateNumbers.Idle,
                Objects.PackMLExecuteStateMachineType_Starting => PackMLStateNumbers.Starting,
                Objects.PackMLExecuteStateMachineType_Execute => PackMLStateNumbers.Execute,
                Objects.PackMLExecuteStateMachineType_Completing => PackMLStateNumbers.Completing,
                Objects.PackMLExecuteStateMachineType_Complete => PackMLStateNumbers.Complete,
                Objects.PackMLExecuteStateMachineType_Holding => PackMLStateNumbers.Holding,
                Objects.PackMLExecuteStateMachineType_Held => PackMLStateNumbers.Held,
                Objects.PackMLExecuteStateMachineType_Unholding => PackMLStateNumbers.Unholding,
                Objects.PackMLExecuteStateMachineType_Suspending => PackMLStateNumbers.Suspending,
                Objects.PackMLExecuteStateMachineType_Suspended => PackMLStateNumbers.Suspended,
                Objects.PackMLExecuteStateMachineType_Unsuspending => PackMLStateNumbers.Unsuspending,
                _ => 0
            };
        }
    }

    /// <summary>
    /// State and transition tables of the OPC 30050 base state machine
    /// (Aborting, Aborted, Cleared).
    /// </summary>
    /// <remarks>
    /// The source generator emits the three PackML state machines as plain
    /// <see cref="FiniteStateMachineState"/> subclasses without tables, so the
    /// stack's <c>SetState</c>, <c>DoCause</c> and <c>DoTransition</c> would
    /// have nothing to look a state up in. The tables below are the OPC 30050
    /// NodeSet's states, transitions (FromState/ToState) and causes
    /// (HasCause), keyed by the generated identifier constants so they cannot
    /// drift from the model. Every transition is flagged as having an effect:
    /// OPC 40200 §5.3 requires an event for each PackML state change.
    /// </remarks>
    public partial class PackMLBaseStateMachineState
    {
        private static readonly ElementInfo[] s_stateTable =
        [
            new ElementInfo(
                Objects.PackMLBaseStateMachineType_Aborting,
                nameof(PackMLStateNumbers.Aborting),
                PackMLStateNumbers.Aborting),
            new ElementInfo(
                Objects.PackMLBaseStateMachineType_Aborted,
                nameof(PackMLStateNumbers.Aborted),
                PackMLStateNumbers.Aborted),
            new ElementInfo(
                Objects.PackMLBaseStateMachineType_Cleared,
                nameof(PackMLStateNumbers.Cleared),
                PackMLStateNumbers.Cleared)
        ];

        private static readonly ElementInfo[] s_transitionTable =
        [
            new ElementInfo(Objects.PackMLBaseStateMachineType_AbortedToCleared, "AbortedToCleared", 1),
            new ElementInfo(Objects.PackMLBaseStateMachineType_AbortingToAborted, "AbortingToAborted", 2),
            new ElementInfo(Objects.PackMLBaseStateMachineType_ClearedToAborting, "ClearedToAborting", 3)
        ];

        private static readonly uint[,] s_transitionMappings = new uint[,]
        {
            {
                Objects.PackMLBaseStateMachineType_AbortedToCleared,
                Objects.PackMLBaseStateMachineType_Aborted,
                Objects.PackMLBaseStateMachineType_Cleared,
                1
            },
            {
                Objects.PackMLBaseStateMachineType_AbortingToAborted,
                Objects.PackMLBaseStateMachineType_Aborting,
                Objects.PackMLBaseStateMachineType_Aborted,
                1
            },
            {
                Objects.PackMLBaseStateMachineType_ClearedToAborting,
                Objects.PackMLBaseStateMachineType_Cleared,
                Objects.PackMLBaseStateMachineType_Aborting,
                1
            }
        };

        private static readonly uint[,] s_causeMappings = new uint[,]
        {
            {
                Methods.PackMLBaseStateMachineType_Clear,
                Objects.PackMLBaseStateMachineType_Aborted,
                Objects.PackMLBaseStateMachineType_AbortedToCleared
            },
            {
                Methods.PackMLBaseStateMachineType_Abort,
                Objects.PackMLBaseStateMachineType_Cleared,
                Objects.PackMLBaseStateMachineType_ClearedToAborting
            }
        };

        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => s_stateTable;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => s_transitionTable;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => s_transitionMappings;

        /// <inheritdoc/>
        protected override uint[,] CauseMappings => s_causeMappings;
    }

    /// <summary>
    /// State and transition tables of the OPC 30050 machine state machine
    /// (Clearing, Stopped, Running, Stopping), the sub-state machine of the
    /// base machine's Cleared state.
    /// </summary>
    /// <remarks>
    /// See <see cref="PackMLBaseStateMachineState"/> for why the tables are
    /// supplied here.
    /// </remarks>
    public partial class PackMLMachineStateMachineState
    {
        private static readonly ElementInfo[] s_stateTable =
        [
            new ElementInfo(
                Objects.PackMLMachineStateMachineType_Clearing,
                nameof(PackMLStateNumbers.Clearing),
                PackMLStateNumbers.Clearing),
            new ElementInfo(
                Objects.PackMLMachineStateMachineType_Stopped,
                nameof(PackMLStateNumbers.Stopped),
                PackMLStateNumbers.Stopped),
            new ElementInfo(
                Objects.PackMLMachineStateMachineType_Running,
                nameof(PackMLStateNumbers.Running),
                PackMLStateNumbers.Running),
            new ElementInfo(
                Objects.PackMLMachineStateMachineType_Stopping,
                nameof(PackMLStateNumbers.Stopping),
                PackMLStateNumbers.Stopping)
        ];

        private static readonly ElementInfo[] s_transitionTable =
        [
            new ElementInfo(Objects.PackMLMachineStateMachineType_ClearingToStopped, "ClearingToStopped", 1),
            new ElementInfo(Objects.PackMLMachineStateMachineType_StoppedToRunning, "StoppedToRunning", 2),
            new ElementInfo(Objects.PackMLMachineStateMachineType_RunningToStopping, "RunningToStopping", 3),
            new ElementInfo(Objects.PackMLMachineStateMachineType_StoppingToStopped, "StoppingToStopped", 4)
        ];

        private static readonly uint[,] s_transitionMappings = new uint[,]
        {
            {
                Objects.PackMLMachineStateMachineType_ClearingToStopped,
                Objects.PackMLMachineStateMachineType_Clearing,
                Objects.PackMLMachineStateMachineType_Stopped,
                1
            },
            {
                Objects.PackMLMachineStateMachineType_StoppedToRunning,
                Objects.PackMLMachineStateMachineType_Stopped,
                Objects.PackMLMachineStateMachineType_Running,
                1
            },
            {
                Objects.PackMLMachineStateMachineType_RunningToStopping,
                Objects.PackMLMachineStateMachineType_Running,
                Objects.PackMLMachineStateMachineType_Stopping,
                1
            },
            {
                Objects.PackMLMachineStateMachineType_StoppingToStopped,
                Objects.PackMLMachineStateMachineType_Stopping,
                Objects.PackMLMachineStateMachineType_Stopped,
                1
            }
        };

        private static readonly uint[,] s_causeMappings = new uint[,]
        {
            {
                Methods.PackMLMachineStateMachineType_Reset,
                Objects.PackMLMachineStateMachineType_Stopped,
                Objects.PackMLMachineStateMachineType_StoppedToRunning
            },
            {
                Methods.PackMLMachineStateMachineType_Stop,
                Objects.PackMLMachineStateMachineType_Running,
                Objects.PackMLMachineStateMachineType_RunningToStopping
            }
        };

        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => s_stateTable;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => s_transitionTable;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => s_transitionMappings;

        /// <inheritdoc/>
        protected override uint[,] CauseMappings => s_causeMappings;
    }

    /// <summary>
    /// State and transition tables of the OPC 30050 execute state machine,
    /// the sub-state machine of the machine state machine's Running state.
    /// </summary>
    /// <remarks>
    /// See <see cref="PackMLBaseStateMachineState"/> for why the tables are
    /// supplied here.
    /// </remarks>
    public partial class PackMLExecuteStateMachineState
    {
        private static readonly ElementInfo[] s_stateTable =
        [
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Resetting,
                nameof(PackMLStateNumbers.Resetting),
                PackMLStateNumbers.Resetting),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Idle,
                nameof(PackMLStateNumbers.Idle),
                PackMLStateNumbers.Idle),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Starting,
                nameof(PackMLStateNumbers.Starting),
                PackMLStateNumbers.Starting),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Execute,
                nameof(PackMLStateNumbers.Execute),
                PackMLStateNumbers.Execute),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Completing,
                nameof(PackMLStateNumbers.Completing),
                PackMLStateNumbers.Completing),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Complete,
                nameof(PackMLStateNumbers.Complete),
                PackMLStateNumbers.Complete),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Holding,
                nameof(PackMLStateNumbers.Holding),
                PackMLStateNumbers.Holding),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Held,
                nameof(PackMLStateNumbers.Held),
                PackMLStateNumbers.Held),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Unholding,
                nameof(PackMLStateNumbers.Unholding),
                PackMLStateNumbers.Unholding),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Suspending,
                nameof(PackMLStateNumbers.Suspending),
                PackMLStateNumbers.Suspending),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Suspended,
                nameof(PackMLStateNumbers.Suspended),
                PackMLStateNumbers.Suspended),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_Unsuspending,
                nameof(PackMLStateNumbers.Unsuspending),
                PackMLStateNumbers.Unsuspending)
        ];

        private static readonly ElementInfo[] s_transitionTable =
        [
            new ElementInfo(Objects.PackMLExecuteStateMachineType_ResettingToIdle, "ResettingToIdle", 1),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_IdleToStarting, "IdleToStarting", 2),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_StartingToExecute, "StartingToExecute", 3),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_ExecuteToSuspending, "ExecuteToSuspending", 4),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_SuspendingToSuspended, "SuspendingToSuspended", 5),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_UnsuspendingToExecute, "UnsuspendingToExecute", 6),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_ExecuteToHolding, "ExecuteToHolding", 7),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_HoldingToHeld, "HoldingToHeld", 8),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_HeldToUnholding, "HeldToUnholding", 9),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_UnholdingToExecute, "UnholdingToExecute", 10),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_ExecuteToCompleting, "ExecuteToCompleting", 11),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_CompletingToComplete, "CompletingToComplete", 12),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_CompleteToResetting, "CompleteToResetting", 13),
            new ElementInfo(
                Objects.PackMLExecuteStateMachineType_SuspendedToUnsuspending,
                "SuspendedToUnsuspending",
                14),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_StartingToHolding, "StartingToHolding", 15),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding, "UnsuspendingToHolding", 16),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_SuspendedToHolding, "SuspendedToHolding", 17),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_SuspendingToHolding, "SuspendingToHolding", 18),
            new ElementInfo(Objects.PackMLExecuteStateMachineType_UnholdingToHolding, "UnholdingToHolding", 19)
        ];

        private static readonly uint[,] s_transitionMappings = new uint[,]
        {
            {
                Objects.PackMLExecuteStateMachineType_ResettingToIdle,
                Objects.PackMLExecuteStateMachineType_Resetting,
                Objects.PackMLExecuteStateMachineType_Idle,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_IdleToStarting,
                Objects.PackMLExecuteStateMachineType_Idle,
                Objects.PackMLExecuteStateMachineType_Starting,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_StartingToExecute,
                Objects.PackMLExecuteStateMachineType_Starting,
                Objects.PackMLExecuteStateMachineType_Execute,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_ExecuteToSuspending,
                Objects.PackMLExecuteStateMachineType_Execute,
                Objects.PackMLExecuteStateMachineType_Suspending,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_SuspendingToSuspended,
                Objects.PackMLExecuteStateMachineType_Suspending,
                Objects.PackMLExecuteStateMachineType_Suspended,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_UnsuspendingToExecute,
                Objects.PackMLExecuteStateMachineType_Unsuspending,
                Objects.PackMLExecuteStateMachineType_Execute,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_ExecuteToHolding,
                Objects.PackMLExecuteStateMachineType_Execute,
                Objects.PackMLExecuteStateMachineType_Holding,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_HoldingToHeld,
                Objects.PackMLExecuteStateMachineType_Holding,
                Objects.PackMLExecuteStateMachineType_Held,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_HeldToUnholding,
                Objects.PackMLExecuteStateMachineType_Held,
                Objects.PackMLExecuteStateMachineType_Unholding,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_UnholdingToExecute,
                Objects.PackMLExecuteStateMachineType_Unholding,
                Objects.PackMLExecuteStateMachineType_Execute,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_ExecuteToCompleting,
                Objects.PackMLExecuteStateMachineType_Execute,
                Objects.PackMLExecuteStateMachineType_Completing,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_CompletingToComplete,
                Objects.PackMLExecuteStateMachineType_Completing,
                Objects.PackMLExecuteStateMachineType_Complete,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_CompleteToResetting,
                Objects.PackMLExecuteStateMachineType_Complete,
                Objects.PackMLExecuteStateMachineType_Resetting,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_SuspendedToUnsuspending,
                Objects.PackMLExecuteStateMachineType_Suspended,
                Objects.PackMLExecuteStateMachineType_Unsuspending,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_StartingToHolding,
                Objects.PackMLExecuteStateMachineType_Starting,
                Objects.PackMLExecuteStateMachineType_Holding,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding,
                Objects.PackMLExecuteStateMachineType_Unsuspending,
                Objects.PackMLExecuteStateMachineType_Holding,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_SuspendedToHolding,
                Objects.PackMLExecuteStateMachineType_Suspended,
                Objects.PackMLExecuteStateMachineType_Holding,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_SuspendingToHolding,
                Objects.PackMLExecuteStateMachineType_Suspending,
                Objects.PackMLExecuteStateMachineType_Holding,
                1
            },
            {
                Objects.PackMLExecuteStateMachineType_UnholdingToHolding,
                Objects.PackMLExecuteStateMachineType_Unholding,
                Objects.PackMLExecuteStateMachineType_Holding,
                1
            }
        };

        private static readonly uint[,] s_causeMappings = new uint[,]
        {
            {
                Methods.PackMLExecuteStateMachineType_Start,
                Objects.PackMLExecuteStateMachineType_Idle,
                Objects.PackMLExecuteStateMachineType_IdleToStarting
            },
            {
                Methods.PackMLExecuteStateMachineType_Suspend,
                Objects.PackMLExecuteStateMachineType_Execute,
                Objects.PackMLExecuteStateMachineType_ExecuteToSuspending
            },
            {
                Methods.PackMLExecuteStateMachineType_Unsuspend,
                Objects.PackMLExecuteStateMachineType_Suspended,
                Objects.PackMLExecuteStateMachineType_SuspendedToUnsuspending
            },
            {
                Methods.PackMLExecuteStateMachineType_Hold,
                Objects.PackMLExecuteStateMachineType_Execute,
                Objects.PackMLExecuteStateMachineType_ExecuteToHolding
            },
            {
                Methods.PackMLExecuteStateMachineType_Hold,
                Objects.PackMLExecuteStateMachineType_Starting,
                Objects.PackMLExecuteStateMachineType_StartingToHolding
            },
            {
                Methods.PackMLExecuteStateMachineType_Hold,
                Objects.PackMLExecuteStateMachineType_Suspending,
                Objects.PackMLExecuteStateMachineType_SuspendingToHolding
            },
            {
                Methods.PackMLExecuteStateMachineType_Hold,
                Objects.PackMLExecuteStateMachineType_Suspended,
                Objects.PackMLExecuteStateMachineType_SuspendedToHolding
            },
            {
                Methods.PackMLExecuteStateMachineType_Hold,
                Objects.PackMLExecuteStateMachineType_Unsuspending,
                Objects.PackMLExecuteStateMachineType_UnsuspendingToHolding
            },
            {
                Methods.PackMLExecuteStateMachineType_Hold,
                Objects.PackMLExecuteStateMachineType_Unholding,
                Objects.PackMLExecuteStateMachineType_UnholdingToHolding
            },
            {
                Methods.PackMLExecuteStateMachineType_Unhold,
                Objects.PackMLExecuteStateMachineType_Held,
                Objects.PackMLExecuteStateMachineType_HeldToUnholding
            },
            {
                Methods.PackMLExecuteStateMachineType_ToComplete,
                Objects.PackMLExecuteStateMachineType_Execute,
                Objects.PackMLExecuteStateMachineType_ExecuteToCompleting
            },
            {
                Methods.PackMLExecuteStateMachineType_Reset,
                Objects.PackMLExecuteStateMachineType_Complete,
                Objects.PackMLExecuteStateMachineType_CompleteToResetting
            }
        };

        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => s_stateTable;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => s_transitionTable;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => s_transitionMappings;

        /// <inheritdoc/>
        protected override uint[,] CauseMappings => s_causeMappings;
    }
}
