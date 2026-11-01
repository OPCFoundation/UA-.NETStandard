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

using CoverIds = global::Opc.Ua.LADS.CoverStateMachineTypeIds;
using FunctionalIds = global::Opc.Ua.LADS.FunctionalStateMachineTypeIds;
using DeviceIds = global::Opc.Ua.LADS.LADSDeviceStateMachineTypeIds;
using RunningIds = global::Opc.Ua.LADS.RunningStateMachineTypeIds;
using MaintenanceIds = global::Opc.Ua.AMB.MaintenanceEventStateMachineTypeIds;
using ItemStateIds = global::Opc.Ua.Machinery.MachineryItemState_StateMachineTypeIds;
using OperationModeIds = global::Opc.Ua.Machinery.MachineryOperationModeStateMachineTypeIds;

namespace Opc.Ua.LADS.Server.StateMachines
{
    /// <summary>
    /// The state and transition tables of every state machine the LADS runtime drives, spelled out from the
    /// generated <c>*TypeIds</c> constants so a model change fails to compile here.
    /// </summary>
    internal static class LadsStateMachineTables
    {
        /// <summary>
        /// Gets the table of <c>CoverStateMachineType</c>.
        /// </summary>
        public static LadsStateTable Cover { get; } = new(
            global::Opc.Ua.LADS.Namespaces.LADS,
            [
                new(CoverIds.StateIds.Closed, "Closed", CoverIds.StateNumbers.Closed),
                new(CoverIds.StateIds.Closing, "Closing", CoverIds.StateNumbers.Closing),
                new(CoverIds.StateIds.Error, "Error", CoverIds.StateNumbers.Error),
                new(CoverIds.StateIds.Locked, "Locked", CoverIds.StateNumbers.Locked),
                new(CoverIds.StateIds.Locking, "Locking", CoverIds.StateNumbers.Locking),
                new(CoverIds.StateIds.Opened, "Opened", CoverIds.StateNumbers.Opened),
                new(CoverIds.StateIds.Opening, "Opening", CoverIds.StateNumbers.Opening),
                new(CoverIds.StateIds.Unlocking, "Unlocking", CoverIds.StateNumbers.Unlocking),
            ],
            [
                new(
                    CoverIds.TransitionIds.ClosedToError,
                    "ClosedToError",
                    CoverIds.TransitionNumbers.ClosedToError,
                    CoverIds.StateIds.Closed,
                    CoverIds.StateIds.Error),
                new(
                    CoverIds.TransitionIds.ClosedToLocked,
                    "ClosedToLocked",
                    CoverIds.TransitionNumbers.ClosedToLocked,
                    CoverIds.StateIds.Closed,
                    CoverIds.StateIds.Locked),
                new(
                    CoverIds.TransitionIds.ClosedToLocking,
                    "ClosedToLocking",
                    CoverIds.TransitionNumbers.ClosedToLocking,
                    CoverIds.StateIds.Closed,
                    CoverIds.StateIds.Locking),
                new(
                    CoverIds.TransitionIds.ClosedToOpened,
                    "ClosedToOpened",
                    CoverIds.TransitionNumbers.ClosedToOpened,
                    CoverIds.StateIds.Closed,
                    CoverIds.StateIds.Opened),
                new(
                    CoverIds.TransitionIds.ClosedToOpening,
                    "ClosedToOpening",
                    CoverIds.TransitionNumbers.ClosedToOpening,
                    CoverIds.StateIds.Closed,
                    CoverIds.StateIds.Opening),
                new(
                    CoverIds.TransitionIds.ClosingToClosed,
                    "ClosingToClosed",
                    CoverIds.TransitionNumbers.ClosingToClosed,
                    CoverIds.StateIds.Closing,
                    CoverIds.StateIds.Closed),
                new(
                    CoverIds.TransitionIds.ErrorToOpened,
                    "ErrorToOpened",
                    CoverIds.TransitionNumbers.ErrorToOpened,
                    CoverIds.StateIds.Error,
                    CoverIds.StateIds.Opened),
                new(
                    CoverIds.TransitionIds.LockedToClosed,
                    "LockedToClosed",
                    CoverIds.TransitionNumbers.LockedToClosed,
                    CoverIds.StateIds.Locked,
                    CoverIds.StateIds.Closed),
                new(
                    CoverIds.TransitionIds.LockedToError,
                    "LockedToError",
                    CoverIds.TransitionNumbers.LockedToError,
                    CoverIds.StateIds.Locked,
                    CoverIds.StateIds.Error),
                new(
                    CoverIds.TransitionIds.LockedToUnlocking,
                    "LockedToUnlocking",
                    CoverIds.TransitionNumbers.LockedToUnlocking,
                    CoverIds.StateIds.Locked,
                    CoverIds.StateIds.Unlocking),
                new(
                    CoverIds.TransitionIds.LockingToLocked,
                    "LockingToLocked",
                    CoverIds.TransitionNumbers.LockingToLocked,
                    CoverIds.StateIds.Locking,
                    CoverIds.StateIds.Locked),
                new(
                    CoverIds.TransitionIds.OpenedToClosed,
                    "OpenedToClosed",
                    CoverIds.TransitionNumbers.OpenedToClosed,
                    CoverIds.StateIds.Opened,
                    CoverIds.StateIds.Closed),
                new(
                    CoverIds.TransitionIds.OpenedToClosing,
                    "OpenedToClosing",
                    CoverIds.TransitionNumbers.OpenedToClosing,
                    CoverIds.StateIds.Opened,
                    CoverIds.StateIds.Closing),
                new(
                    CoverIds.TransitionIds.OpeningToOpened,
                    "OpeningToOpened",
                    CoverIds.TransitionNumbers.OpeningToOpened,
                    CoverIds.StateIds.Opening,
                    CoverIds.StateIds.Opened),
                new(
                    CoverIds.TransitionIds.UnlockingToClosed,
                    "UnlockingToClosed",
                    CoverIds.TransitionNumbers.UnlockingToClosed,
                    CoverIds.StateIds.Unlocking,
                    CoverIds.StateIds.Closed),
            ]);

        /// <summary>
        /// Gets the table of <c>FunctionalStateMachineType</c>.
        /// </summary>
        public static LadsStateTable Functional { get; } = new(
            global::Opc.Ua.LADS.Namespaces.LADS,
            [
                new(FunctionalIds.StateIds.Aborted, "Aborted", FunctionalIds.StateNumbers.Aborted),
                new(FunctionalIds.StateIds.Aborting, "Aborting", FunctionalIds.StateNumbers.Aborting),
                new(FunctionalIds.StateIds.Clearing, "Clearing", FunctionalIds.StateNumbers.Clearing),
                new(FunctionalIds.StateIds.Running, "Running", FunctionalIds.StateNumbers.Running),
                new(FunctionalIds.StateIds.Stopped, "Stopped", FunctionalIds.StateNumbers.Stopped),
                new(FunctionalIds.StateIds.Stopping, "Stopping", FunctionalIds.StateNumbers.Stopping),
            ],
            [
                new(
                    FunctionalIds.TransitionIds.AbortedToClearing,
                    "AbortedToClearing",
                    FunctionalIds.TransitionNumbers.AbortedToClearing,
                    FunctionalIds.StateIds.Aborted,
                    FunctionalIds.StateIds.Clearing),
                new(
                    FunctionalIds.TransitionIds.AbortingToAborted,
                    "AbortingToAborted",
                    FunctionalIds.TransitionNumbers.AbortingToAborted,
                    FunctionalIds.StateIds.Aborting,
                    FunctionalIds.StateIds.Aborted),
                new(
                    FunctionalIds.TransitionIds.ClearingToStopped,
                    "ClearingToStopped",
                    FunctionalIds.TransitionNumbers.ClearingToStopped,
                    FunctionalIds.StateIds.Clearing,
                    FunctionalIds.StateIds.Stopped),
                new(
                    FunctionalIds.TransitionIds.RunningToAborting,
                    "RunningToAborting",
                    FunctionalIds.TransitionNumbers.RunningToAborting,
                    FunctionalIds.StateIds.Running,
                    FunctionalIds.StateIds.Aborting),
                new(
                    FunctionalIds.TransitionIds.RunningToStopping,
                    "RunningToStopping",
                    FunctionalIds.TransitionNumbers.RunningToStopping,
                    FunctionalIds.StateIds.Running,
                    FunctionalIds.StateIds.Stopping),
                new(
                    FunctionalIds.TransitionIds.StoppedToRunning,
                    "StoppedToRunning",
                    FunctionalIds.TransitionNumbers.StoppedToRunning,
                    FunctionalIds.StateIds.Stopped,
                    FunctionalIds.StateIds.Running),
                new(
                    FunctionalIds.TransitionIds.StoppingToStopped,
                    "StoppingToStopped",
                    FunctionalIds.TransitionNumbers.StoppingToStopped,
                    FunctionalIds.StateIds.Stopping,
                    FunctionalIds.StateIds.Stopped),
            ]);

        /// <summary>
        /// Gets the table of <c>LADSDeviceStateMachineType</c>.
        /// </summary>
        public static LadsStateTable Device { get; } = new(
            global::Opc.Ua.LADS.Namespaces.LADS,
            [
                new(DeviceIds.StateIds.Initialization, "Initialization", DeviceIds.StateNumbers.Initialization),
                new(DeviceIds.StateIds.Operate, "Operate", DeviceIds.StateNumbers.Operate),
                new(DeviceIds.StateIds.Shutdown, "Shutdown", DeviceIds.StateNumbers.Shutdown),
                new(DeviceIds.StateIds.Sleep, "Sleep", DeviceIds.StateNumbers.Sleep),
            ],
            [
                new(
                    DeviceIds.TransitionIds.InitializationToOperate,
                    "InitializationToOperate",
                    DeviceIds.TransitionNumbers.InitializationToOperate,
                    DeviceIds.StateIds.Initialization,
                    DeviceIds.StateIds.Operate),
                new(
                    DeviceIds.TransitionIds.OperateToShutdown,
                    "OperateToShutdown",
                    DeviceIds.TransitionNumbers.OperateToShutdown,
                    DeviceIds.StateIds.Operate,
                    DeviceIds.StateIds.Shutdown),
                new(
                    DeviceIds.TransitionIds.OperateToSleep,
                    "OperateToSleep",
                    DeviceIds.TransitionNumbers.OperateToSleep,
                    DeviceIds.StateIds.Operate,
                    DeviceIds.StateIds.Sleep),
                new(
                    DeviceIds.TransitionIds.SleepToOperate,
                    "SleepToOperate",
                    DeviceIds.TransitionNumbers.SleepToOperate,
                    DeviceIds.StateIds.Sleep,
                    DeviceIds.StateIds.Operate),
            ]);

        /// <summary>
        /// Gets the table of <c>RunningStateMachineType</c>.
        /// </summary>
        public static LadsStateTable Running { get; } = new(
            global::Opc.Ua.LADS.Namespaces.LADS,
            [
                new(RunningIds.StateIds.Complete, "Complete", RunningIds.StateNumbers.Complete),
                new(RunningIds.StateIds.Completing, "Completing", RunningIds.StateNumbers.Completing),
                new(RunningIds.StateIds.Execute, "Execute", RunningIds.StateNumbers.Execute),
                new(RunningIds.StateIds.Held, "Held", RunningIds.StateNumbers.Held),
                new(RunningIds.StateIds.Holding, "Holding", RunningIds.StateNumbers.Holding),
                new(RunningIds.StateIds.Idle, "Idle", RunningIds.StateNumbers.Idle),
                new(RunningIds.StateIds.Resetting, "Resetting", RunningIds.StateNumbers.Resetting),
                new(RunningIds.StateIds.Starting, "Starting", RunningIds.StateNumbers.Starting),
                new(RunningIds.StateIds.Suspended, "Suspended", RunningIds.StateNumbers.Suspended),
                new(RunningIds.StateIds.Suspending, "Suspending", RunningIds.StateNumbers.Suspending),
                new(RunningIds.StateIds.Unholding, "Unholding", RunningIds.StateNumbers.Unholding),
                new(RunningIds.StateIds.Unsuspending, "Unsuspending", RunningIds.StateNumbers.Unsuspending),
            ],
            [
                new(
                    RunningIds.TransitionIds.CompleteToResetting,
                    "CompleteToResetting",
                    RunningIds.TransitionNumbers.CompleteToResetting,
                    RunningIds.StateIds.Complete,
                    RunningIds.StateIds.Resetting),
                new(
                    RunningIds.TransitionIds.CompletingToComplete,
                    "CompletingToComplete",
                    RunningIds.TransitionNumbers.CompletingToComplete,
                    RunningIds.StateIds.Completing,
                    RunningIds.StateIds.Complete),
                new(
                    RunningIds.TransitionIds.ExecuteToCompleting,
                    "ExecuteToCompleting",
                    RunningIds.TransitionNumbers.ExecuteToCompleting,
                    RunningIds.StateIds.Execute,
                    RunningIds.StateIds.Completing),
                new(
                    RunningIds.TransitionIds.ExecuteToHolding,
                    "ExecuteToHolding",
                    RunningIds.TransitionNumbers.ExecuteToHolding,
                    RunningIds.StateIds.Execute,
                    RunningIds.StateIds.Holding),
                new(
                    RunningIds.TransitionIds.ExecuteToSuspending,
                    "ExecuteToSuspending",
                    RunningIds.TransitionNumbers.ExecuteToSuspending,
                    RunningIds.StateIds.Execute,
                    RunningIds.StateIds.Suspending),
                new(
                    RunningIds.TransitionIds.HeldToUnholding,
                    "HeldToUnholding",
                    RunningIds.TransitionNumbers.HeldToUnholding,
                    RunningIds.StateIds.Held,
                    RunningIds.StateIds.Unholding),
                new(
                    RunningIds.TransitionIds.HoldingToHeld,
                    "HoldingToHeld",
                    RunningIds.TransitionNumbers.HoldingToHeld,
                    RunningIds.StateIds.Holding,
                    RunningIds.StateIds.Held),
                new(
                    RunningIds.TransitionIds.IdleToStarting,
                    "IdleToStarting",
                    RunningIds.TransitionNumbers.IdleToStarting,
                    RunningIds.StateIds.Idle,
                    RunningIds.StateIds.Starting),
                new(
                    RunningIds.TransitionIds.ResettingToIdle,
                    "ResettingToIdle",
                    RunningIds.TransitionNumbers.ResettingToIdle,
                    RunningIds.StateIds.Resetting,
                    RunningIds.StateIds.Idle),
                new(
                    RunningIds.TransitionIds.StartingToExecute,
                    "StartingToExecute",
                    RunningIds.TransitionNumbers.StartingToExecute,
                    RunningIds.StateIds.Starting,
                    RunningIds.StateIds.Execute),
                new(
                    RunningIds.TransitionIds.StartingToHolding,
                    "StartingToHolding",
                    RunningIds.TransitionNumbers.StartingToHolding,
                    RunningIds.StateIds.Starting,
                    RunningIds.StateIds.Holding),
                new(
                    RunningIds.TransitionIds.SuspendedToHolding,
                    "SuspendedToHolding",
                    RunningIds.TransitionNumbers.SuspendedToHolding,
                    RunningIds.StateIds.Suspended,
                    RunningIds.StateIds.Holding),
                new(
                    RunningIds.TransitionIds.SuspendedToUnsuspending,
                    "SuspendedToUnsuspending",
                    RunningIds.TransitionNumbers.SuspendedToUnsuspending,
                    RunningIds.StateIds.Suspended,
                    RunningIds.StateIds.Unsuspending),
                new(
                    RunningIds.TransitionIds.SuspendingToHolding,
                    "SuspendingToHolding",
                    RunningIds.TransitionNumbers.SuspendingToHolding,
                    RunningIds.StateIds.Suspending,
                    RunningIds.StateIds.Holding),
                new(
                    RunningIds.TransitionIds.SuspendingToSuspended,
                    "SuspendingToSuspended",
                    RunningIds.TransitionNumbers.SuspendingToSuspended,
                    RunningIds.StateIds.Suspending,
                    RunningIds.StateIds.Suspended),
                new(
                    RunningIds.TransitionIds.UnholdingToExecute,
                    "UnholdingToExecute",
                    RunningIds.TransitionNumbers.UnholdingToExecute,
                    RunningIds.StateIds.Unholding,
                    RunningIds.StateIds.Execute),
                new(
                    RunningIds.TransitionIds.UnholdingToHolding,
                    "UnholdingToHolding",
                    RunningIds.TransitionNumbers.UnholdingToHolding,
                    RunningIds.StateIds.Unholding,
                    RunningIds.StateIds.Holding),
                new(
                    RunningIds.TransitionIds.UnsuspendingToExecute,
                    "UnsuspendingToExecute",
                    RunningIds.TransitionNumbers.UnsuspendingToExecute,
                    RunningIds.StateIds.Unsuspending,
                    RunningIds.StateIds.Execute),
                new(
                    RunningIds.TransitionIds.UnsuspendingToHolding,
                    "UnsuspendingToHolding",
                    RunningIds.TransitionNumbers.UnsuspendingToHolding,
                    RunningIds.StateIds.Unsuspending,
                    RunningIds.StateIds.Holding),
            ]);

        /// <summary>
        /// Gets the table of <c>MaintenanceEventStateMachineType</c>.
        /// </summary>
        public static LadsStateTable Maintenance { get; } = new(
            global::Opc.Ua.AMB.Namespaces.AMB,
            [
                new(MaintenanceIds.StateIds.Executing, "Executing", MaintenanceIds.StateNumbers.Executing),
                new(MaintenanceIds.StateIds.Finished, "Finished", MaintenanceIds.StateNumbers.Finished),
                new(MaintenanceIds.StateIds.Planned, "Planned", MaintenanceIds.StateNumbers.Planned),
            ],
            [
                new(
                    MaintenanceIds.TransitionIds.FromExecutingToFinished,
                    "FromExecutingToFinished",
                    MaintenanceIds.TransitionNumbers.FromExecutingToFinished,
                    MaintenanceIds.StateIds.Executing,
                    MaintenanceIds.StateIds.Finished),
                new(
                    MaintenanceIds.TransitionIds.FromFinishedToPlanned,
                    "FromFinishedToPlanned",
                    MaintenanceIds.TransitionNumbers.FromFinishedToPlanned,
                    MaintenanceIds.StateIds.Finished,
                    MaintenanceIds.StateIds.Planned),
                new(
                    MaintenanceIds.TransitionIds.FromPlannedToExecuting,
                    "FromPlannedToExecuting",
                    MaintenanceIds.TransitionNumbers.FromPlannedToExecuting,
                    MaintenanceIds.StateIds.Planned,
                    MaintenanceIds.StateIds.Executing),
            ]);

        /// <summary>
        /// Gets the table of <c>MachineryItemState_StateMachineType</c>.
        /// </summary>
        public static LadsStateTable ItemState { get; } = new(
            global::Opc.Ua.Machinery.Namespaces.Machinery,
            [
                new(ItemStateIds.StateIds.Executing, "Executing", ItemStateIds.StateNumbers.Executing),
                new(ItemStateIds.StateIds.NotAvailable, "NotAvailable", ItemStateIds.StateNumbers.NotAvailable),
                new(ItemStateIds.StateIds.NotExecuting, "NotExecuting", ItemStateIds.StateNumbers.NotExecuting),
                new(ItemStateIds.StateIds.OutOfService, "OutOfService", ItemStateIds.StateNumbers.OutOfService),
            ],
            [
                new(
                    ItemStateIds.TransitionIds.FromExecutingToExecuting,
                    "FromExecutingToExecuting",
                    ItemStateIds.TransitionNumbers.FromExecutingToExecuting,
                    ItemStateIds.StateIds.Executing,
                    ItemStateIds.StateIds.Executing),
                new(
                    ItemStateIds.TransitionIds.FromExecutingToNotAvailable,
                    "FromExecutingToNotAvailable",
                    ItemStateIds.TransitionNumbers.FromExecutingToNotAvailable,
                    ItemStateIds.StateIds.Executing,
                    ItemStateIds.StateIds.NotAvailable),
                new(
                    ItemStateIds.TransitionIds.FromExecutingToNotExecuting,
                    "FromExecutingToNotExecuting",
                    ItemStateIds.TransitionNumbers.FromExecutingToNotExecuting,
                    ItemStateIds.StateIds.Executing,
                    ItemStateIds.StateIds.NotExecuting),
                new(
                    ItemStateIds.TransitionIds.FromExecutingToOutOfService,
                    "FromExecutingToOutOfService",
                    ItemStateIds.TransitionNumbers.FromExecutingToOutOfService,
                    ItemStateIds.StateIds.Executing,
                    ItemStateIds.StateIds.OutOfService),
                new(
                    ItemStateIds.TransitionIds.FromNotAvailableToExecuting,
                    "FromNotAvailableToExecuting",
                    ItemStateIds.TransitionNumbers.FromNotAvailableToExecuting,
                    ItemStateIds.StateIds.NotAvailable,
                    ItemStateIds.StateIds.Executing),
                new(
                    ItemStateIds.TransitionIds.FromNotAvailableToNotAvailable,
                    "FromNotAvailableToNotAvailable",
                    ItemStateIds.TransitionNumbers.FromNotAvailableToNotAvailable,
                    ItemStateIds.StateIds.NotAvailable,
                    ItemStateIds.StateIds.NotAvailable),
                new(
                    ItemStateIds.TransitionIds.FromNotAvailableToNotExecuting,
                    "FromNotAvailableToNotExecuting",
                    ItemStateIds.TransitionNumbers.FromNotAvailableToNotExecuting,
                    ItemStateIds.StateIds.NotAvailable,
                    ItemStateIds.StateIds.NotExecuting),
                new(
                    ItemStateIds.TransitionIds.FromNotAvailableToOutOfService,
                    "FromNotAvailableToOutOfService",
                    ItemStateIds.TransitionNumbers.FromNotAvailableToOutOfService,
                    ItemStateIds.StateIds.NotAvailable,
                    ItemStateIds.StateIds.OutOfService),
                new(
                    ItemStateIds.TransitionIds.FromNotExecutingToExecuting,
                    "FromNotExecutingToExecuting",
                    ItemStateIds.TransitionNumbers.FromNotExecutingToExecuting,
                    ItemStateIds.StateIds.NotExecuting,
                    ItemStateIds.StateIds.Executing),
                new(
                    ItemStateIds.TransitionIds.FromNotExecutingToNotAvailable,
                    "FromNotExecutingToNotAvailable",
                    ItemStateIds.TransitionNumbers.FromNotExecutingToNotAvailable,
                    ItemStateIds.StateIds.NotExecuting,
                    ItemStateIds.StateIds.NotAvailable),
                new(
                    ItemStateIds.TransitionIds.FromNotExecutingToNotExecuting,
                    "FromNotExecutingToNotExecuting",
                    ItemStateIds.TransitionNumbers.FromNotExecutingToNotExecuting,
                    ItemStateIds.StateIds.NotExecuting,
                    ItemStateIds.StateIds.NotExecuting),
                new(
                    ItemStateIds.TransitionIds.FromNotExecutingToOutOfService,
                    "FromNotExecutingToOutOfService",
                    ItemStateIds.TransitionNumbers.FromNotExecutingToOutOfService,
                    ItemStateIds.StateIds.NotExecuting,
                    ItemStateIds.StateIds.OutOfService),
                new(
                    ItemStateIds.TransitionIds.FromOutOfServiceToExecuting,
                    "FromOutOfServiceToExecuting",
                    ItemStateIds.TransitionNumbers.FromOutOfServiceToExecuting,
                    ItemStateIds.StateIds.OutOfService,
                    ItemStateIds.StateIds.Executing),
                new(
                    ItemStateIds.TransitionIds.FromOutOfServiceToNotAvailable,
                    "FromOutOfServiceToNotAvailable",
                    ItemStateIds.TransitionNumbers.FromOutOfServiceToNotAvailable,
                    ItemStateIds.StateIds.OutOfService,
                    ItemStateIds.StateIds.NotAvailable),
                new(
                    ItemStateIds.TransitionIds.FromOutOfServiceToNotExecuting,
                    "FromOutOfServiceToNotExecuting",
                    ItemStateIds.TransitionNumbers.FromOutOfServiceToNotExecuting,
                    ItemStateIds.StateIds.OutOfService,
                    ItemStateIds.StateIds.NotExecuting),
                new(
                    ItemStateIds.TransitionIds.FromOutOfServiceToOutOfService,
                    "FromOutOfServiceToOutOfService",
                    ItemStateIds.TransitionNumbers.FromOutOfServiceToOutOfService,
                    ItemStateIds.StateIds.OutOfService,
                    ItemStateIds.StateIds.OutOfService),
            ]);

        /// <summary>
        /// Gets the table of <c>MachineryOperationModeStateMachineType</c>.
        /// </summary>
        public static LadsStateTable OperationMode { get; } = new(
            global::Opc.Ua.Machinery.Namespaces.Machinery,
            [
                new(OperationModeIds.StateIds.Maintenance, "Maintenance", OperationModeIds.StateNumbers.Maintenance),
                new(OperationModeIds.StateIds.None, "None", OperationModeIds.StateNumbers.None),
                new(OperationModeIds.StateIds.Processing, "Processing", OperationModeIds.StateNumbers.Processing),
                new(OperationModeIds.StateIds.Setup, "Setup", OperationModeIds.StateNumbers.Setup),
            ],
            [
                new(
                    OperationModeIds.TransitionIds.FromMaintenanceToMaintenance,
                    "FromMaintenanceToMaintenance",
                    OperationModeIds.TransitionNumbers.FromMaintenanceToMaintenance,
                    OperationModeIds.StateIds.Maintenance,
                    OperationModeIds.StateIds.Maintenance),
                new(
                    OperationModeIds.TransitionIds.FromMaintenanceToNone,
                    "FromMaintenanceToNone",
                    OperationModeIds.TransitionNumbers.FromMaintenanceToNone,
                    OperationModeIds.StateIds.Maintenance,
                    OperationModeIds.StateIds.None),
                new(
                    OperationModeIds.TransitionIds.FromMaintenanceToProcessing,
                    "FromMaintenanceToProcessing",
                    OperationModeIds.TransitionNumbers.FromMaintenanceToProcessing,
                    OperationModeIds.StateIds.Maintenance,
                    OperationModeIds.StateIds.Processing),
                new(
                    OperationModeIds.TransitionIds.FromMaintenanceToSetup,
                    "FromMaintenanceToSetup",
                    OperationModeIds.TransitionNumbers.FromMaintenanceToSetup,
                    OperationModeIds.StateIds.Maintenance,
                    OperationModeIds.StateIds.Setup),
                new(
                    OperationModeIds.TransitionIds.FromNoneToMaintenance,
                    "FromNoneToMaintenance",
                    OperationModeIds.TransitionNumbers.FromNoneToMaintenance,
                    OperationModeIds.StateIds.None,
                    OperationModeIds.StateIds.Maintenance),
                new(
                    OperationModeIds.TransitionIds.FromNoneToNone,
                    "FromNoneToNone",
                    OperationModeIds.TransitionNumbers.FromNoneToNone,
                    OperationModeIds.StateIds.None,
                    OperationModeIds.StateIds.None),
                new(
                    OperationModeIds.TransitionIds.FromNoneToProcessing,
                    "FromNoneToProcessing",
                    OperationModeIds.TransitionNumbers.FromNoneToProcessing,
                    OperationModeIds.StateIds.None,
                    OperationModeIds.StateIds.Processing),
                new(
                    OperationModeIds.TransitionIds.FromNoneToSetup,
                    "FromNoneToSetup",
                    OperationModeIds.TransitionNumbers.FromNoneToSetup,
                    OperationModeIds.StateIds.None,
                    OperationModeIds.StateIds.Setup),
                new(
                    OperationModeIds.TransitionIds.FromProcessingToMaintenance,
                    "FromProcessingToMaintenance",
                    OperationModeIds.TransitionNumbers.FromProcessingToMaintenance,
                    OperationModeIds.StateIds.Processing,
                    OperationModeIds.StateIds.Maintenance),
                new(
                    OperationModeIds.TransitionIds.FromProcessingToNone,
                    "FromProcessingToNone",
                    OperationModeIds.TransitionNumbers.FromProcessingToNone,
                    OperationModeIds.StateIds.Processing,
                    OperationModeIds.StateIds.None),
                new(
                    OperationModeIds.TransitionIds.FromProcessingToProcessing,
                    "FromProcessingToProcessing",
                    OperationModeIds.TransitionNumbers.FromProcessingToProcessing,
                    OperationModeIds.StateIds.Processing,
                    OperationModeIds.StateIds.Processing),
                new(
                    OperationModeIds.TransitionIds.FromProcessingToSetup,
                    "FromProcessingToSetup",
                    OperationModeIds.TransitionNumbers.FromProcessingToSetup,
                    OperationModeIds.StateIds.Processing,
                    OperationModeIds.StateIds.Setup),
                new(
                    OperationModeIds.TransitionIds.FromSetupToMaintenance,
                    "FromSetupToMaintenance",
                    OperationModeIds.TransitionNumbers.FromSetupToMaintenance,
                    OperationModeIds.StateIds.Setup,
                    OperationModeIds.StateIds.Maintenance),
                new(
                    OperationModeIds.TransitionIds.FromSetupToNone,
                    "FromSetupToNone",
                    OperationModeIds.TransitionNumbers.FromSetupToNone,
                    OperationModeIds.StateIds.Setup,
                    OperationModeIds.StateIds.None),
                new(
                    OperationModeIds.TransitionIds.FromSetupToProcessing,
                    "FromSetupToProcessing",
                    OperationModeIds.TransitionNumbers.FromSetupToProcessing,
                    OperationModeIds.StateIds.Setup,
                    OperationModeIds.StateIds.Processing),
                new(
                    OperationModeIds.TransitionIds.FromSetupToSetup,
                    "FromSetupToSetup",
                    OperationModeIds.TransitionNumbers.FromSetupToSetup,
                    OperationModeIds.StateIds.Setup,
                    OperationModeIds.StateIds.Setup),
            ]);
    }
}
