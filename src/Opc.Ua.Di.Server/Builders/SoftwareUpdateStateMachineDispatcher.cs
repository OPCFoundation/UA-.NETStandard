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
using Microsoft.Extensions.Logging;
using Opc.Ua.Server.StateMachines;

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Well-known state / transition metadata for the four DI
    /// software-update state machines (OPC 10000-100 §10.3).
    /// </summary>
    /// <remarks>
    /// The DI source-generator does not yet emit Part-16 cause /
    /// transition tables for the generated <c>*StateMachineState</c>
    /// subclasses, so this dispatcher writes <c>CurrentState</c> and
    /// <c>LastTransition</c> directly instead of routing through
    /// <see cref="FiniteStateMachineState"/>'s
    /// <c>SetState</c> / <c>DoCause</c> path (which depends on
    /// overridden <c>StateTable</c> / <c>TransitionTable</c>).
    /// When the generator gains table-emission support, this helper
    /// can be replaced by
    /// <c>StateMachineBuilder.For(sm, ctx).WithCause(...)</c> chains.
    /// </remarks>
    internal static class SoftwareUpdateStateMachineDispatcher
    {
        internal const uint PrepareForUpdate_Idle = PrepareForUpdateStateMachineTypeIds.StateIds.Idle;
        internal const uint PrepareForUpdate_Preparing = PrepareForUpdateStateMachineTypeIds.StateIds.Preparing;
        internal const uint PrepareForUpdate_PreparedForUpdate = PrepareForUpdateStateMachineTypeIds.StateIds.PreparedForUpdate;

        internal const uint Installation_Idle = InstallationStateMachineTypeIds.StateIds.Idle;
        internal const uint Installation_Installing = InstallationStateMachineTypeIds.StateIds.Installing;
        internal const uint Installation_Error = InstallationStateMachineTypeIds.StateIds.Error;

        internal const uint PowerCycle_NotWaiting = PowerCycleStateMachineTypeIds.StateIds.NotWaitingForPowerCycle;
        internal const uint PowerCycle_Waiting = PowerCycleStateMachineTypeIds.StateIds.WaitingForPowerCycle;

        internal const uint Confirmation_NotWaitingForConfirm = ConfirmationStateMachineTypeIds.StateIds.NotWaitingForConfirm;
        internal const uint Confirmation_WaitingForConfirm = ConfirmationStateMachineTypeIds.StateIds.WaitingForConfirm;

        internal const uint PrepareForUpdate_IdleToPreparing = PrepareForUpdateStateMachineTypeIds.TransitionIds.IdleToPreparing;
        internal const uint PrepareForUpdate_PreparingToIdle = PrepareForUpdateStateMachineTypeIds.TransitionIds.PreparingToIdle;
        internal const uint PrepareForUpdate_PreparingToPreparedForUpdate = PrepareForUpdateStateMachineTypeIds.TransitionIds.PreparingToPreparedForUpdate;
        internal const uint PrepareForUpdate_PreparedForUpdateToResuming = PrepareForUpdateStateMachineTypeIds.TransitionIds.PreparedForUpdateToResuming;

        internal const uint Installation_IdleToInstalling = InstallationStateMachineTypeIds.TransitionIds.IdleToInstalling;
        internal const uint Installation_InstallingToIdle = InstallationStateMachineTypeIds.TransitionIds.InstallingToIdle;
        internal const uint Installation_InstallingToError = InstallationStateMachineTypeIds.TransitionIds.InstallingToError;
        internal const uint Installation_ErrorToIdle = InstallationStateMachineTypeIds.TransitionIds.ErrorToIdle;

        internal const uint Confirmation_NotWaitingToWaiting = ConfirmationStateMachineTypeIds.TransitionIds.NotWaitingForConfirmToWaitingForConfirm;
        internal const uint Confirmation_WaitingToNotWaiting = ConfirmationStateMachineTypeIds.TransitionIds.WaitingForConfirmToNotWaitingForConfirm;

        private static readonly ArrayOf<FiniteStateMachineEntry> s_states =
        [
            new(PrepareForUpdate_Idle,
             PrepareForUpdateStateMachineTypeIds.StateNumbers.Idle, "Idle"),
            new(PrepareForUpdate_Preparing,
             PrepareForUpdateStateMachineTypeIds.StateNumbers.Preparing, "Preparing"),
            new(PrepareForUpdate_PreparedForUpdate,
             PrepareForUpdateStateMachineTypeIds.StateNumbers.PreparedForUpdate, "PreparedForUpdate"),
            new(Installation_Idle,
             InstallationStateMachineTypeIds.StateNumbers.Idle, "Idle"),
            new(Installation_Installing,
             InstallationStateMachineTypeIds.StateNumbers.Installing, "Installing"),
            new(Installation_Error,
             InstallationStateMachineTypeIds.StateNumbers.Error, "Error"),
            new(PowerCycle_NotWaiting,
             PowerCycleStateMachineTypeIds.StateNumbers.NotWaitingForPowerCycle, "NotWaitingForPowerCycle"),
            new(PowerCycle_Waiting,
             PowerCycleStateMachineTypeIds.StateNumbers.WaitingForPowerCycle, "WaitingForPowerCycle"),
            new(Confirmation_NotWaitingForConfirm,
             ConfirmationStateMachineTypeIds.StateNumbers.NotWaitingForConfirm, "NotWaitingForConfirm"),
            new(Confirmation_WaitingForConfirm,
             ConfirmationStateMachineTypeIds.StateNumbers.WaitingForConfirm, "WaitingForConfirm")
        ];

        private static readonly ArrayOf<FiniteStateMachineEntry> s_transitions =
        [
            new(PrepareForUpdate_IdleToPreparing,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.IdleToPreparing,
             "IdleToPreparing"),
            new(PrepareForUpdate_PreparingToIdle,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.PreparingToIdle,
             "PreparingToIdle"),
            new(PrepareForUpdate_PreparingToPreparedForUpdate,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.PreparingToPreparedForUpdate,
             "PreparingToPreparedForUpdate"),
            new(PrepareForUpdate_PreparedForUpdateToResuming,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.PreparedForUpdateToResuming,
             "PreparedForUpdateToResuming"),
            new(Installation_IdleToInstalling,
             InstallationStateMachineTypeIds.TransitionNumbers.IdleToInstalling,
             "IdleToInstalling"),
            new(Installation_InstallingToIdle,
             InstallationStateMachineTypeIds.TransitionNumbers.InstallingToIdle,
             "InstallingToIdle"),
            new(Installation_InstallingToError,
             InstallationStateMachineTypeIds.TransitionNumbers.InstallingToError,
             "InstallingToError"),
            new(Installation_ErrorToIdle,
             InstallationStateMachineTypeIds.TransitionNumbers.ErrorToIdle,
             "ErrorToIdle"),
            new(Confirmation_NotWaitingToWaiting,
             ConfirmationStateMachineTypeIds.TransitionNumbers.NotWaitingForConfirmToWaitingForConfirm,
             "NotWaitingForConfirmToWaitingForConfirm"),
            new(Confirmation_WaitingToNotWaiting,
             ConfirmationStateMachineTypeIds.TransitionNumbers.WaitingForConfirmToNotWaitingForConfirm,
             "WaitingForConfirmToNotWaitingForConfirm")
        ];

        /// <summary>
        /// Initialise the FSM's <c>CurrentState</c> to its standard
        /// initial state (Idle / NotWaiting…). Idempotent.
        /// </summary>
        internal static void InitializeToInitialState(
            FiniteStateMachineState sm,
            uint initialStateId,
            ushort diNamespaceIndex,
            ISystemContext context)
        {
            CreateDispatcher(diNamespaceIndex)
                .InitializeToInitialState(sm, initialStateId, context);
        }

        /// <summary>
        /// Writes <paramref name="stateId"/> into <paramref name="sm"/>'s
        /// <c>CurrentState</c> variable.
        /// </summary>
        internal static void ApplyState(
            FiniteStateMachineState sm,
            uint stateId,
            ushort diNamespaceIndex,
            ISystemContext context)
        {
            CreateDispatcher(diNamespaceIndex).ApplyState(sm, stateId, context);
        }

        /// <summary>
        /// Writes <paramref name="transitionId"/> into <paramref name="sm"/>'s
        /// optional <c>LastTransition</c> variable.
        /// </summary>
        internal static void ApplyTransition(
            FiniteStateMachineState sm,
            uint transitionId,
            ushort diNamespaceIndex,
            ISystemContext context)
        {
            CreateDispatcher(diNamespaceIndex).ApplyTransition(sm, transitionId, context);
        }

        /// <summary>
        /// Atomically writes a state transition.
        /// </summary>
        internal static void Move(
            FiniteStateMachineState sm,
            uint toStateId,
            uint transitionId,
            ushort diNamespaceIndex,
            ISystemContext context)
        {
            CreateDispatcher(diNamespaceIndex).Move(sm, toStateId, transitionId, context);
        }

        /// <summary>
        /// Updates <c>InstallationStateMachine.PercentComplete</c> if
        /// the optional child is present. No-op otherwise.
        /// </summary>
        internal static void SetPercentComplete(
            InstallationStateMachineState sm,
            byte percent,
            ISystemContext context)
        {
            if (sm?.PercentComplete is null)
            {
                return;
            }
            sm.PercentComplete.Value = percent;
            sm.PercentComplete.ClearChangeMasks(context, includeChildren: false);
        }

        /// <summary>
        /// Fires a state-change hook, swallowing and logging any
        /// exception so the SU method invocation isn't aborted by
        /// instrumentation faults.
        /// </summary>
        internal static async ValueTask FireAsync(
            Func<ISoftwareUpdateContext, SoftwareUpdateStateChange, ValueTask>? handler,
            ISoftwareUpdateContext context,
            SoftwareUpdateStateChange change,
            ILogger? logger,
            CancellationToken cancellationToken)
        {
            if (handler is null)
            {
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await handler(context, change).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.SoftwareUpdateStateChangeHookThrew(ex, change.Phase);
            }
        }

        private static FiniteStateMachineDispatcher CreateDispatcher(ushort namespaceIndex)
        {
            return new FiniteStateMachineDispatcher(
                namespaceIndex,
                s_states,
                s_transitions);
        }

    }

    internal static partial class SoftwareUpdateStateMachineDispatcherLog
    {
        [LoggerMessage(EventId = DiServerEventIds.SoftwareUpdateStateMachineDispatcher + 0, Level = LogLevel.Warning,
            Message = "Software-update state-change hook threw for phase {Phase}; swallowed.")]
        public static partial void SoftwareUpdateStateChangeHookThrew(
            this ILogger logger,
            Exception ex,
            SoftwareUpdatePhase phase);
    }
}
