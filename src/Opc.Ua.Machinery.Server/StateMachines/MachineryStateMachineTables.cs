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


using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Def = Opc.Ua.Machinery.Server.StateMachines.MachineryStateMachineDriver;
using ItemIds = Opc.Ua.Machinery.MachineryItemState_StateMachineTypeIds;
using ModeIds = Opc.Ua.Machinery.MachineryOperationModeStateMachineTypeIds;

namespace Opc.Ua.Machinery.Server.StateMachines
{
    /// <summary>
    /// The state and transition tables of the two OPC 40001-1 state machines,
    /// plus the controllers that drive them.
    /// </summary>
    /// <remarks>
    /// Both machines declare four states and sixteen transitions — every
    /// ordered pair including the self-transitions — and neither declares a
    /// cause. The tables below are spelled out from the generated
    /// <c>*TypeIds</c> constants, so a change to the vendored NodeSet that the
    /// identifier drift guard lets through would still fail to compile here.
    /// </remarks>
    internal static class MachineryStateMachineTables
    {
        public static IReadOnlyList<Def.StateDefinition> ItemStates { get; } =
            new Def.StateDefinition[]
            {
                new(
                    ItemIds.StateIds.Executing,
                    "Executing",
                    ItemIds.StateNumbers.Executing),
                new(
                    ItemIds.StateIds.NotAvailable,
                    "NotAvailable",
                    ItemIds.StateNumbers.NotAvailable),
                new(
                    ItemIds.StateIds.NotExecuting,
                    "NotExecuting",
                    ItemIds.StateNumbers.NotExecuting),
                new(
                    ItemIds.StateIds.OutOfService,
                    "OutOfService",
                    ItemIds.StateNumbers.OutOfService),
            };

        public static IReadOnlyList<Def.TransitionDefinition> ItemTransitions { get; } =
            new Def.TransitionDefinition[]
            {
                new(
                    ItemIds.TransitionIds.FromExecutingToExecuting,
                    "FromExecutingToExecuting",
                    ItemIds.TransitionNumbers.FromExecutingToExecuting,
                    ItemIds.StateIds.Executing,
                    ItemIds.StateIds.Executing),
                new(
                    ItemIds.TransitionIds.FromExecutingToNotAvailable,
                    "FromExecutingToNotAvailable",
                    ItemIds.TransitionNumbers.FromExecutingToNotAvailable,
                    ItemIds.StateIds.Executing,
                    ItemIds.StateIds.NotAvailable),
                new(
                    ItemIds.TransitionIds.FromExecutingToNotExecuting,
                    "FromExecutingToNotExecuting",
                    ItemIds.TransitionNumbers.FromExecutingToNotExecuting,
                    ItemIds.StateIds.Executing,
                    ItemIds.StateIds.NotExecuting),
                new(
                    ItemIds.TransitionIds.FromExecutingToOutOfService,
                    "FromExecutingToOutOfService",
                    ItemIds.TransitionNumbers.FromExecutingToOutOfService,
                    ItemIds.StateIds.Executing,
                    ItemIds.StateIds.OutOfService),
                new(
                    ItemIds.TransitionIds.FromNotAvailableToExecuting,
                    "FromNotAvailableToExecuting",
                    ItemIds.TransitionNumbers.FromNotAvailableToExecuting,
                    ItemIds.StateIds.NotAvailable,
                    ItemIds.StateIds.Executing),
                new(
                    ItemIds.TransitionIds.FromNotAvailableToNotAvailable,
                    "FromNotAvailableToNotAvailable",
                    ItemIds.TransitionNumbers.FromNotAvailableToNotAvailable,
                    ItemIds.StateIds.NotAvailable,
                    ItemIds.StateIds.NotAvailable),
                new(
                    ItemIds.TransitionIds.FromNotAvailableToNotExecuting,
                    "FromNotAvailableToNotExecuting",
                    ItemIds.TransitionNumbers.FromNotAvailableToNotExecuting,
                    ItemIds.StateIds.NotAvailable,
                    ItemIds.StateIds.NotExecuting),
                new(
                    ItemIds.TransitionIds.FromNotAvailableToOutOfService,
                    "FromNotAvailableToOutOfService",
                    ItemIds.TransitionNumbers.FromNotAvailableToOutOfService,
                    ItemIds.StateIds.NotAvailable,
                    ItemIds.StateIds.OutOfService),
                new(
                    ItemIds.TransitionIds.FromNotExecutingToExecuting,
                    "FromNotExecutingToExecuting",
                    ItemIds.TransitionNumbers.FromNotExecutingToExecuting,
                    ItemIds.StateIds.NotExecuting,
                    ItemIds.StateIds.Executing),
                new(
                    ItemIds.TransitionIds.FromNotExecutingToNotAvailable,
                    "FromNotExecutingToNotAvailable",
                    ItemIds.TransitionNumbers.FromNotExecutingToNotAvailable,
                    ItemIds.StateIds.NotExecuting,
                    ItemIds.StateIds.NotAvailable),
                new(
                    ItemIds.TransitionIds.FromNotExecutingToNotExecuting,
                    "FromNotExecutingToNotExecuting",
                    ItemIds.TransitionNumbers.FromNotExecutingToNotExecuting,
                    ItemIds.StateIds.NotExecuting,
                    ItemIds.StateIds.NotExecuting),
                new(
                    ItemIds.TransitionIds.FromNotExecutingToOutOfService,
                    "FromNotExecutingToOutOfService",
                    ItemIds.TransitionNumbers.FromNotExecutingToOutOfService,
                    ItemIds.StateIds.NotExecuting,
                    ItemIds.StateIds.OutOfService),
                new(
                    ItemIds.TransitionIds.FromOutOfServiceToExecuting,
                    "FromOutOfServiceToExecuting",
                    ItemIds.TransitionNumbers.FromOutOfServiceToExecuting,
                    ItemIds.StateIds.OutOfService,
                    ItemIds.StateIds.Executing),
                new(
                    ItemIds.TransitionIds.FromOutOfServiceToNotAvailable,
                    "FromOutOfServiceToNotAvailable",
                    ItemIds.TransitionNumbers.FromOutOfServiceToNotAvailable,
                    ItemIds.StateIds.OutOfService,
                    ItemIds.StateIds.NotAvailable),
                new(
                    ItemIds.TransitionIds.FromOutOfServiceToNotExecuting,
                    "FromOutOfServiceToNotExecuting",
                    ItemIds.TransitionNumbers.FromOutOfServiceToNotExecuting,
                    ItemIds.StateIds.OutOfService,
                    ItemIds.StateIds.NotExecuting),
                new(
                    ItemIds.TransitionIds.FromOutOfServiceToOutOfService,
                    "FromOutOfServiceToOutOfService",
                    ItemIds.TransitionNumbers.FromOutOfServiceToOutOfService,
                    ItemIds.StateIds.OutOfService,
                    ItemIds.StateIds.OutOfService),
            };

        public static IReadOnlyList<Def.StateDefinition> OperationModeStates { get; } =
            new Def.StateDefinition[]
            {
                new(
                    ModeIds.StateIds.Maintenance,
                    "Maintenance",
                    ModeIds.StateNumbers.Maintenance),
                new(
                    ModeIds.StateIds.None,
                    "None",
                    ModeIds.StateNumbers.None),
                new(
                    ModeIds.StateIds.Processing,
                    "Processing",
                    ModeIds.StateNumbers.Processing),
                new(
                    ModeIds.StateIds.Setup,
                    "Setup",
                    ModeIds.StateNumbers.Setup),
            };

        public static IReadOnlyList<Def.TransitionDefinition> OperationModeTransitions { get; } =
            new Def.TransitionDefinition[]
            {
                new(
                    ModeIds.TransitionIds.FromMaintenanceToMaintenance,
                    "FromMaintenanceToMaintenance",
                    ModeIds.TransitionNumbers.FromMaintenanceToMaintenance,
                    ModeIds.StateIds.Maintenance,
                    ModeIds.StateIds.Maintenance),
                new(
                    ModeIds.TransitionIds.FromMaintenanceToNone,
                    "FromMaintenanceToNone",
                    ModeIds.TransitionNumbers.FromMaintenanceToNone,
                    ModeIds.StateIds.Maintenance,
                    ModeIds.StateIds.None),
                new(
                    ModeIds.TransitionIds.FromMaintenanceToProcessing,
                    "FromMaintenanceToProcessing",
                    ModeIds.TransitionNumbers.FromMaintenanceToProcessing,
                    ModeIds.StateIds.Maintenance,
                    ModeIds.StateIds.Processing),
                new(
                    ModeIds.TransitionIds.FromMaintenanceToSetup,
                    "FromMaintenanceToSetup",
                    ModeIds.TransitionNumbers.FromMaintenanceToSetup,
                    ModeIds.StateIds.Maintenance,
                    ModeIds.StateIds.Setup),
                new(
                    ModeIds.TransitionIds.FromNoneToMaintenance,
                    "FromNoneToMaintenance",
                    ModeIds.TransitionNumbers.FromNoneToMaintenance,
                    ModeIds.StateIds.None,
                    ModeIds.StateIds.Maintenance),
                new(
                    ModeIds.TransitionIds.FromNoneToNone,
                    "FromNoneToNone",
                    ModeIds.TransitionNumbers.FromNoneToNone,
                    ModeIds.StateIds.None,
                    ModeIds.StateIds.None),
                new(
                    ModeIds.TransitionIds.FromNoneToProcessing,
                    "FromNoneToProcessing",
                    ModeIds.TransitionNumbers.FromNoneToProcessing,
                    ModeIds.StateIds.None,
                    ModeIds.StateIds.Processing),
                new(
                    ModeIds.TransitionIds.FromNoneToSetup,
                    "FromNoneToSetup",
                    ModeIds.TransitionNumbers.FromNoneToSetup,
                    ModeIds.StateIds.None,
                    ModeIds.StateIds.Setup),
                new(
                    ModeIds.TransitionIds.FromProcessingToMaintenance,
                    "FromProcessingToMaintenance",
                    ModeIds.TransitionNumbers.FromProcessingToMaintenance,
                    ModeIds.StateIds.Processing,
                    ModeIds.StateIds.Maintenance),
                new(
                    ModeIds.TransitionIds.FromProcessingToNone,
                    "FromProcessingToNone",
                    ModeIds.TransitionNumbers.FromProcessingToNone,
                    ModeIds.StateIds.Processing,
                    ModeIds.StateIds.None),
                new(
                    ModeIds.TransitionIds.FromProcessingToProcessing,
                    "FromProcessingToProcessing",
                    ModeIds.TransitionNumbers.FromProcessingToProcessing,
                    ModeIds.StateIds.Processing,
                    ModeIds.StateIds.Processing),
                new(
                    ModeIds.TransitionIds.FromProcessingToSetup,
                    "FromProcessingToSetup",
                    ModeIds.TransitionNumbers.FromProcessingToSetup,
                    ModeIds.StateIds.Processing,
                    ModeIds.StateIds.Setup),
                new(
                    ModeIds.TransitionIds.FromSetupToMaintenance,
                    "FromSetupToMaintenance",
                    ModeIds.TransitionNumbers.FromSetupToMaintenance,
                    ModeIds.StateIds.Setup,
                    ModeIds.StateIds.Maintenance),
                new(
                    ModeIds.TransitionIds.FromSetupToNone,
                    "FromSetupToNone",
                    ModeIds.TransitionNumbers.FromSetupToNone,
                    ModeIds.StateIds.Setup,
                    ModeIds.StateIds.None),
                new(
                    ModeIds.TransitionIds.FromSetupToProcessing,
                    "FromSetupToProcessing",
                    ModeIds.TransitionNumbers.FromSetupToProcessing,
                    ModeIds.StateIds.Setup,
                    ModeIds.StateIds.Processing),
                new(
                    ModeIds.TransitionIds.FromSetupToSetup,
                    "FromSetupToSetup",
                    ModeIds.TransitionNumbers.FromSetupToSetup,
                    ModeIds.StateIds.Setup,
                    ModeIds.StateIds.Setup),
            };

        public static uint ToStateId(MachineryItemStateValue state)
        {
            return state switch
            {
                MachineryItemStateValue.NotAvailable =>
                    ItemIds.StateIds.NotAvailable,
                MachineryItemStateValue.OutOfService =>
                    ItemIds.StateIds.OutOfService,
                MachineryItemStateValue.NotExecuting =>
                    ItemIds.StateIds.NotExecuting,
                MachineryItemStateValue.Executing =>
                    ItemIds.StateIds.Executing,
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "'{0}' is not a MachineryItemState value.",
                    state)
            };
        }

        public static MachineryItemStateValue ToItemState(uint stateId)
        {
            if (stateId == ItemIds.StateIds.OutOfService)
            {
                return MachineryItemStateValue.OutOfService;
            }
            if (stateId == ItemIds.StateIds.NotExecuting)
            {
                return MachineryItemStateValue.NotExecuting;
            }
            if (stateId == ItemIds.StateIds.Executing)
            {
                return MachineryItemStateValue.Executing;
            }
            return MachineryItemStateValue.NotAvailable;
        }

        public static uint ToStateId(MachineryOperationModeValue mode)
        {
            return mode switch
            {
                MachineryOperationModeValue.None =>
                    ModeIds.StateIds.None,
                MachineryOperationModeValue.Maintenance =>
                    ModeIds.StateIds.Maintenance,
                MachineryOperationModeValue.Setup =>
                    ModeIds.StateIds.Setup,
                MachineryOperationModeValue.Processing =>
                    ModeIds.StateIds.Processing,
                _ => throw ServiceResultException.Create(
                    StatusCodes.BadInvalidArgument,
                    "'{0}' is not a MachineryOperationMode value.",
                    mode)
            };
        }

        public static MachineryOperationModeValue ToOperationMode(uint stateId)
        {
            if (stateId == ModeIds.StateIds.Maintenance)
            {
                return MachineryOperationModeValue.Maintenance;
            }
            if (stateId == ModeIds.StateIds.Setup)
            {
                return MachineryOperationModeValue.Setup;
            }
            if (stateId == ModeIds.StateIds.Processing)
            {
                return MachineryOperationModeValue.Processing;
            }
            return MachineryOperationModeValue.None;
        }
    }

    /// <summary>
    /// Implements <see cref="IMachineryItemStateController"/> over the
    /// generated <c>MachineryItemState_StateMachineState</c>, driving it with a
    /// <see cref="MachineryStateMachineDriver"/> loaded with the item-state
    /// tables of <see cref="MachineryStateMachineTables"/>.
    /// </summary>
    internal sealed class MachineryItemStateController : IMachineryItemStateController
    {
        public MachineryItemStateController(
            MachineryItemState_StateMachineState stateMachine,
            ISystemContext context,
            ushort modelNamespaceIndex,
            MachineryItemStateValue initialState)
        {
            m_driver = new MachineryStateMachineDriver(
                stateMachine,
                context,
                modelNamespaceIndex,
                MachineryStateMachineTables.ItemStates,
                MachineryStateMachineTables.ItemTransitions);
            m_driver.SetInitialState(MachineryStateMachineTables.ToStateId(initialState));
        }

        public NodeId NodeId => m_driver.NodeId;

        public MachineryItemStateValue CurrentState =>
            MachineryStateMachineTables.ToItemState(m_driver.CurrentStateId);

        public ValueTask<bool> SetStateAsync(
            MachineryItemStateValue state,
            CancellationToken cancellationToken = default)
        {
            return m_driver.TransitionToAsync(
                MachineryStateMachineTables.ToStateId(state),
                cancellationToken);
        }

        private readonly MachineryStateMachineDriver m_driver;
    }

    /// <summary>
    /// Implements <see cref="IMachineryOperationModeController"/> over the
    /// generated <c>MachineryOperationModeStateMachineState</c>, driving it
    /// with a <see cref="MachineryStateMachineDriver"/> loaded with the
    /// operation-mode tables of <see cref="MachineryStateMachineTables"/>.
    /// </summary>
    internal sealed class MachineryOperationModeController : IMachineryOperationModeController
    {
        public MachineryOperationModeController(
            MachineryOperationModeStateMachineState stateMachine,
            ISystemContext context,
            ushort modelNamespaceIndex,
            MachineryOperationModeValue initialMode)
        {
            m_driver = new MachineryStateMachineDriver(
                stateMachine,
                context,
                modelNamespaceIndex,
                MachineryStateMachineTables.OperationModeStates,
                MachineryStateMachineTables.OperationModeTransitions);
            m_driver.SetInitialState(MachineryStateMachineTables.ToStateId(initialMode));
        }

        public NodeId NodeId => m_driver.NodeId;

        public MachineryOperationModeValue CurrentMode =>
            MachineryStateMachineTables.ToOperationMode(m_driver.CurrentStateId);

        public ValueTask<bool> SetModeAsync(
            MachineryOperationModeValue mode,
            CancellationToken cancellationToken = default)
        {
            return m_driver.TransitionToAsync(
                MachineryStateMachineTables.ToStateId(mode),
                cancellationToken);
        }

        private readonly MachineryStateMachineDriver m_driver;
    }
}
