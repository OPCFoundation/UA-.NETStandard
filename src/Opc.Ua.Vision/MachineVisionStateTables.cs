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

using Automatic = Opc.Ua.Vision.MachineVision.VisionAutomaticModeStateMachineTypeIds;
using Control = Opc.Ua.Vision.MachineVision.VisionStateMachineTypeIds;
using Step = Opc.Ua.Vision.MachineVision.VisionStepModelStateMachineTypeIds;

namespace Opc.Ua.Vision.MachineVision
{
    /// <summary>
    /// Completes the generated state with the OPC 40100-1 state table, as in PackML.
    /// State elements retain their published namespace and identifiers.
    /// </summary>
    public partial class VisionStateMachineState
    {
        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => s_states;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => s_transitions;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => s_mappings;

        /// <inheritdoc/>
        protected override uint[,] CauseMappings => s_causes;

        private static readonly ElementInfo[] s_states =
        [
            new(Control.StateIds.Preoperational, "Preoperational", Control.StateNumbers.Preoperational),
            new(Control.StateIds.Halted, "Halted", Control.StateNumbers.Halted),
            new(Control.StateIds.Error, "Error", Control.StateNumbers.Error),
            new(Control.StateIds.Operational, "Operational", Control.StateNumbers.Operational)
        ];

        private static readonly ElementInfo[] s_transitions =
        [
            new(Control.TransitionIds.ErrorToHalted, "ErrorToHalted", Control.TransitionNumbers.ErrorToHalted),
            new(Control.TransitionIds.ErrorToHaltedAuto, "ErrorToHaltedAuto",
                Control.TransitionNumbers.ErrorToHaltedAuto),
            new(Control.TransitionIds.ErrorToOperationalAuto, "ErrorToOperationalAuto",
                Control.TransitionNumbers.ErrorToOperationalAuto),
            new(Control.TransitionIds.ErrorToPreoperational, "ErrorToPreoperational",
                Control.TransitionNumbers.ErrorToPreoperational),
            new(Control.TransitionIds.ErrorToPreoperationalAuto, "ErrorToPreoperationalAuto",
                Control.TransitionNumbers.ErrorToPreoperationalAuto),
            new(Control.TransitionIds.HaltedToPreoperational, "HaltedToPreoperational",
                Control.TransitionNumbers.HaltedToPreoperational),
            new(Control.TransitionIds.HaltedToPreoperationalAuto, "HaltedToPreoperationalAuto",
                Control.TransitionNumbers.HaltedToPreoperationalAuto),
            new(Control.TransitionIds.OperationalToErrorAuto, "OperationalToErrorAuto",
                Control.TransitionNumbers.OperationalToErrorAuto),
            new(Control.TransitionIds.OperationalToHalted, "OperationalToHalted",
                Control.TransitionNumbers.OperationalToHalted),
            new(Control.TransitionIds.OperationalToHaltedAuto, "OperationalToHaltedAuto",
                Control.TransitionNumbers.OperationalToHaltedAuto),
            new(Control.TransitionIds.OperationalToPreoperational, "OperationalToPreoperational",
                Control.TransitionNumbers.OperationalToPreoperational),
            new(Control.TransitionIds.OperationalToPreoperationalAuto, "OperationalToPreoperationalAuto",
                Control.TransitionNumbers.OperationalToPreoperationalAuto),
            new(Control.TransitionIds.PreoperationalToErrorAuto, "PreoperationalToErrorAuto",
                Control.TransitionNumbers.PreoperationalToErrorAuto),
            new(Control.TransitionIds.PreoperationalToHalted, "PreoperationalToHalted",
                Control.TransitionNumbers.PreoperationalToHalted),
            new(Control.TransitionIds.PreoperationalToHaltedAuto, "PreoperationalToHaltedAuto",
                Control.TransitionNumbers.PreoperationalToHaltedAuto),
            new(Control.TransitionIds.PreoperationalToInitialized, "PreoperationalToInitialized",
                Control.TransitionNumbers.PreoperationalToInitialized),
            new(Control.TransitionIds.PreoperationalToInitializedAuto, "PreoperationalToInitializedAuto",
                Control.TransitionNumbers.PreoperationalToInitializedAuto),
            new(Control.TransitionIds.PreoperationalToOperational, "PreoperationalToOperational",
                Control.TransitionNumbers.PreoperationalToOperational),
            new(Control.TransitionIds.PreoperationalToOperationalAuto, "PreoperationalToOperationalAuto",
                Control.TransitionNumbers.PreoperationalToOperationalAuto)
        ];

        // Cross-level Initialized transitions enter Operational in this machine.
        // The runtime initializes its automatic-mode child in the same operation.
        private static readonly uint[,] s_mappings =
        {
            { Control.TransitionIds.ErrorToHalted, Control.StateIds.Error, Control.StateIds.Halted, 1 },
            { Control.TransitionIds.ErrorToHaltedAuto, Control.StateIds.Error, Control.StateIds.Halted, 1 },
            { Control.TransitionIds.ErrorToOperationalAuto, Control.StateIds.Error, Control.StateIds.Operational, 1 },
            { Control.TransitionIds.ErrorToPreoperational, Control.StateIds.Error, Control.StateIds.Preoperational, 1 },
            { Control.TransitionIds.ErrorToPreoperationalAuto, Control.StateIds.Error,
                Control.StateIds.Preoperational, 1 },
            { Control.TransitionIds.HaltedToPreoperational, Control.StateIds.Halted,
                Control.StateIds.Preoperational, 1 },
            { Control.TransitionIds.HaltedToPreoperationalAuto, Control.StateIds.Halted,
                Control.StateIds.Preoperational, 1 },
            { Control.TransitionIds.OperationalToErrorAuto, Control.StateIds.Operational, Control.StateIds.Error, 1 },
            { Control.TransitionIds.OperationalToHalted, Control.StateIds.Operational, Control.StateIds.Halted, 1 },
            { Control.TransitionIds.OperationalToHaltedAuto, Control.StateIds.Operational, Control.StateIds.Halted, 1 },
            { Control.TransitionIds.OperationalToPreoperational, Control.StateIds.Operational,
                Control.StateIds.Preoperational, 1 },
            { Control.TransitionIds.OperationalToPreoperationalAuto, Control.StateIds.Operational,
                Control.StateIds.Preoperational, 1 },
            { Control.TransitionIds.PreoperationalToErrorAuto, Control.StateIds.Preoperational,
                Control.StateIds.Error, 1 },
            { Control.TransitionIds.PreoperationalToHalted, Control.StateIds.Preoperational,
                Control.StateIds.Halted, 1 },
            { Control.TransitionIds.PreoperationalToHaltedAuto, Control.StateIds.Preoperational,
                Control.StateIds.Halted, 1 },
            { Control.TransitionIds.PreoperationalToInitialized, Control.StateIds.Preoperational,
                Control.StateIds.Operational, 1 },
            { Control.TransitionIds.PreoperationalToInitializedAuto, Control.StateIds.Preoperational,
                Control.StateIds.Operational, 1 },
            { Control.TransitionIds.PreoperationalToOperational, Control.StateIds.Preoperational,
                Control.StateIds.Operational, 1 },
            { Control.TransitionIds.PreoperationalToOperationalAuto, Control.StateIds.Preoperational,
                Control.StateIds.Operational, 1 }
        };

        private static readonly uint[,] s_causes =
        {
            { Methods.VisionStateMachineType_Halt, Control.StateIds.Preoperational,
                Control.TransitionIds.PreoperationalToHalted },
            { Methods.VisionStateMachineType_Halt, Control.StateIds.Error, Control.TransitionIds.ErrorToHalted },
            { Methods.VisionStateMachineType_Halt, Control.StateIds.Operational,
                Control.TransitionIds.OperationalToHalted },
            { Methods.VisionStateMachineType_Reset, Control.StateIds.Halted,
                Control.TransitionIds.HaltedToPreoperational },
            { Methods.VisionStateMachineType_Reset, Control.StateIds.Error,
                Control.TransitionIds.ErrorToPreoperational },
            { Methods.VisionStateMachineType_Reset, Control.StateIds.Operational,
                Control.TransitionIds.OperationalToPreoperational },
            { Methods.VisionStateMachineType_SelectModeAutomatic, Control.StateIds.Preoperational,
                Control.TransitionIds.PreoperationalToInitialized }
        };
    }

    /// <summary>
    /// State and transition tables of OPC 40100-1 automatic mode.
    /// </summary>
    public partial class VisionAutomaticModeStateMachineState
    {
        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => s_states;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => s_transitions;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => s_mappings;

        /// <inheritdoc/>
        protected override uint[,] CauseMappings => s_causes;

        private static readonly ElementInfo[] s_states =
        [
            new(Automatic.StateIds.Initialized, "Initialized", Automatic.StateNumbers.Initialized),
            new(Automatic.StateIds.Ready, "Ready", Automatic.StateNumbers.Ready),
            new(Automatic.StateIds.SingleExecution, "SingleExecution", Automatic.StateNumbers.SingleExecution),
            new(Automatic.StateIds.ContinuousExecution, "ContinuousExecution",
                Automatic.StateNumbers.ContinuousExecution)
        ];

        private static readonly ElementInfo[] s_transitions =
        [
            new(Automatic.TransitionIds.InitializedToReadyAuto, "InitializedToReadyAuto",
                Automatic.TransitionNumbers.InitializedToReadyAuto),
            new(Automatic.TransitionIds.InitializedToReadyProduct, "InitializedToReadyProduct",
                Automatic.TransitionNumbers.InitializedToReadyProduct),
            new(Automatic.TransitionIds.InitializedToReadyRecipe, "InitializedToReadyRecipe",
                Automatic.TransitionNumbers.InitializedToReadyRecipe),
            new(Automatic.TransitionIds.ReadyToInitializedAuto, "ReadyToInitializedAuto",
                Automatic.TransitionNumbers.ReadyToInitializedAuto),
            new(Automatic.TransitionIds.ReadyToInitializedProduct, "ReadyToInitializedProduct",
                Automatic.TransitionNumbers.ReadyToInitializedProduct),
            new(Automatic.TransitionIds.ReadyToInitializedRecipe, "ReadyToInitializedRecipe",
                Automatic.TransitionNumbers.ReadyToInitializedRecipe),
            new(Automatic.TransitionIds.ReadyToSingleExecution, "ReadyToSingleExecution",
                Automatic.TransitionNumbers.ReadyToSingleExecution),
            new(Automatic.TransitionIds.ReadyToSingleExecutionAuto, "ReadyToSingleExecutionAuto",
                Automatic.TransitionNumbers.ReadyToSingleExecutionAuto),
            new(Automatic.TransitionIds.ReadyToContinuousExecution, "ReadyToContinuousExecution",
                Automatic.TransitionNumbers.ReadyToContinuousExecution),
            new(Automatic.TransitionIds.ReadyToContinuousExecutionAuto, "ReadyToContinuousExecutionAuto",
                Automatic.TransitionNumbers.ReadyToContinuousExecutionAuto),
            new(Automatic.TransitionIds.SingleExecutionToReadyAbort, "SingleExecutionToReadyAbort",
                Automatic.TransitionNumbers.SingleExecutionToReadyAbort),
            new(Automatic.TransitionIds.SingleExecutionToReadyAuto, "SingleExecutionToReadyAuto",
                Automatic.TransitionNumbers.SingleExecutionToReadyAuto),
            new(Automatic.TransitionIds.SingleExecutionToReadyStop, "SingleExecutionToReadyStop",
                Automatic.TransitionNumbers.SingleExecutionToReadyStop),
            new(Automatic.TransitionIds.ContinuousExecutionToReadyAbort, "ContinuousExecutionToReadyAbort",
                Automatic.TransitionNumbers.ContinuousExecutionToReadyAbort),
            new(Automatic.TransitionIds.ContinuousExecutionToReadyAuto, "ContinuousExecutionToReadyAuto",
                Automatic.TransitionNumbers.ContinuousExecutionToReadyAuto),
            new(Automatic.TransitionIds.ContinuousExecutionToReadyStop, "ContinuousExecutionToReadyStop",
                Automatic.TransitionNumbers.ContinuousExecutionToReadyStop)
        ];

        private static readonly uint[,] s_mappings =
        {
            { Automatic.TransitionIds.InitializedToReadyAuto, Automatic.StateIds.Initialized,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.InitializedToReadyProduct, Automatic.StateIds.Initialized,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.InitializedToReadyRecipe, Automatic.StateIds.Initialized,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.ReadyToInitializedAuto, Automatic.StateIds.Ready,
                Automatic.StateIds.Initialized, 1 },
            { Automatic.TransitionIds.ReadyToInitializedProduct, Automatic.StateIds.Ready,
                Automatic.StateIds.Initialized, 1 },
            { Automatic.TransitionIds.ReadyToInitializedRecipe, Automatic.StateIds.Ready,
                Automatic.StateIds.Initialized, 1 },
            { Automatic.TransitionIds.ReadyToSingleExecution, Automatic.StateIds.Ready,
                Automatic.StateIds.SingleExecution, 1 },
            { Automatic.TransitionIds.ReadyToSingleExecutionAuto, Automatic.StateIds.Ready,
                Automatic.StateIds.SingleExecution, 1 },
            { Automatic.TransitionIds.ReadyToContinuousExecution, Automatic.StateIds.Ready,
                Automatic.StateIds.ContinuousExecution, 1 },
            { Automatic.TransitionIds.ReadyToContinuousExecutionAuto, Automatic.StateIds.Ready,
                Automatic.StateIds.ContinuousExecution, 1 },
            { Automatic.TransitionIds.SingleExecutionToReadyAbort, Automatic.StateIds.SingleExecution,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.SingleExecutionToReadyAuto, Automatic.StateIds.SingleExecution,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.SingleExecutionToReadyStop, Automatic.StateIds.SingleExecution,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.ContinuousExecutionToReadyAbort, Automatic.StateIds.ContinuousExecution,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.ContinuousExecutionToReadyAuto, Automatic.StateIds.ContinuousExecution,
                Automatic.StateIds.Ready, 1 },
            { Automatic.TransitionIds.ContinuousExecutionToReadyStop, Automatic.StateIds.ContinuousExecution,
                Automatic.StateIds.Ready, 1 }
        };

        private static readonly uint[,] s_causes =
        {
            { Methods.RecipeManagementType_PrepareRecipe, Automatic.StateIds.Initialized,
                Automatic.TransitionIds.InitializedToReadyRecipe },
            { Methods.RecipeManagementType_PrepareProduct, Automatic.StateIds.Initialized,
                Automatic.TransitionIds.InitializedToReadyProduct },
            { Methods.RecipeManagementType_UnprepareRecipe, Automatic.StateIds.Ready,
                Automatic.TransitionIds.ReadyToInitializedRecipe },
            { Methods.RecipeManagementType_UnprepareProduct, Automatic.StateIds.Ready,
                Automatic.TransitionIds.ReadyToInitializedProduct },
            { Methods.VisionAutomaticModeStateMachineType_StartSingleJob, Automatic.StateIds.Ready,
                Automatic.TransitionIds.ReadyToSingleExecution },
            { Methods.VisionAutomaticModeStateMachineType_StartContinuous, Automatic.StateIds.Ready,
                Automatic.TransitionIds.ReadyToContinuousExecution },
            { Methods.VisionAutomaticModeStateMachineType_Stop, Automatic.StateIds.SingleExecution,
                Automatic.TransitionIds.SingleExecutionToReadyStop },
            { Methods.VisionAutomaticModeStateMachineType_Stop, Automatic.StateIds.ContinuousExecution,
                Automatic.TransitionIds.ContinuousExecutionToReadyStop },
            { Methods.VisionAutomaticModeStateMachineType_Abort, Automatic.StateIds.SingleExecution,
                Automatic.TransitionIds.SingleExecutionToReadyAbort },
            { Methods.VisionAutomaticModeStateMachineType_Abort, Automatic.StateIds.ContinuousExecution,
                Automatic.TransitionIds.ContinuousExecutionToReadyAbort }
        };
    }

    /// <summary>
    /// State and transition tables of the reusable OPC 40100-1 synchronization step model.
    /// </summary>
    public partial class VisionStepModelStateMachineState
    {
        /// <inheritdoc/>
        protected override ElementInfo[] StateTable => s_states;

        /// <inheritdoc/>
        protected override ElementInfo[] TransitionTable => s_transitions;

        /// <inheritdoc/>
        protected override uint[,] TransitionMappings => s_mappings;

        /// <inheritdoc/>
        protected override uint[,] CauseMappings => s_causes;

        private static readonly ElementInfo[] s_states =
        [
            new(Step.StateIds.Entry, "Entry", Step.StateNumbers.Entry),
            new(Step.StateIds.Exit, "Exit", Step.StateNumbers.Exit),
            new(Step.StateIds.Wait, "Wait", Step.StateNumbers.Wait),
            new(Step.StateIds.Step, "Step", Step.StateNumbers.Step)
        ];

        private static readonly ElementInfo[] s_transitions =
        [
            new(Step.TransitionIds.EntryToExitAuto, "EntryToExitAuto", Step.TransitionNumbers.EntryToExitAuto),
            new(Step.TransitionIds.EntryToWaitAuto, "EntryToWaitAuto", Step.TransitionNumbers.EntryToWaitAuto),
            new(Step.TransitionIds.WaitToStep, "WaitToStep", Step.TransitionNumbers.WaitToStep),
            new(Step.TransitionIds.WaitToStepAuto, "WaitToStepAuto", Step.TransitionNumbers.WaitToStepAuto),
            new(Step.TransitionIds.StepToExitAuto, "StepToExitAuto", Step.TransitionNumbers.StepToExitAuto),
            new(Step.TransitionIds.StepToWaitAuto, "StepToWaitAuto", Step.TransitionNumbers.StepToWaitAuto)
        ];

        private static readonly uint[,] s_mappings =
        {
            { Step.TransitionIds.EntryToExitAuto, Step.StateIds.Entry, Step.StateIds.Exit, 1 },
            { Step.TransitionIds.EntryToWaitAuto, Step.StateIds.Entry, Step.StateIds.Wait, 1 },
            { Step.TransitionIds.WaitToStep, Step.StateIds.Wait, Step.StateIds.Step, 1 },
            { Step.TransitionIds.WaitToStepAuto, Step.StateIds.Wait, Step.StateIds.Step, 1 },
            { Step.TransitionIds.StepToExitAuto, Step.StateIds.Step, Step.StateIds.Exit, 1 },
            { Step.TransitionIds.StepToWaitAuto, Step.StateIds.Step, Step.StateIds.Wait, 1 }
        };

        private static readonly uint[,] s_causes =
        {
            { Methods.VisionStepModelStateMachineType_Sync, Step.StateIds.Wait, Step.TransitionIds.WaitToStep }
        };
    }
}
