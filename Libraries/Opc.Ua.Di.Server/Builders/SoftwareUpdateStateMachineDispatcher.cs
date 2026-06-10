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
        /// <summary>
        /// ---- well-known state identifiers (delegated to typed Ids classes) ----
        /// PrepareForUpdate
        /// </summary>
        internal const uint PrepareForUpdate_Idle = PrepareForUpdateStateMachineTypeIds.StateIds.Idle;
        internal const uint PrepareForUpdate_Preparing = PrepareForUpdateStateMachineTypeIds.StateIds.Preparing;
        internal const uint PrepareForUpdate_PreparedForUpdate = PrepareForUpdateStateMachineTypeIds.StateIds.PreparedForUpdate;

        /// <summary>
        /// Installation
        /// </summary>
        internal const uint Installation_Idle = InstallationStateMachineTypeIds.StateIds.Idle;
        internal const uint Installation_Installing = InstallationStateMachineTypeIds.StateIds.Installing;
        internal const uint Installation_Error = InstallationStateMachineTypeIds.StateIds.Error;

        /// <summary>
        /// PowerCycle
        /// </summary>
        internal const uint PowerCycle_NotWaiting = PowerCycleStateMachineTypeIds.StateIds.NotWaitingForPowerCycle;
        internal const uint PowerCycle_Waiting = PowerCycleStateMachineTypeIds.StateIds.WaitingForPowerCycle;

        /// <summary>
        /// Confirmation
        /// </summary>
        internal const uint Confirmation_NotWaitingForConfirm = ConfirmationStateMachineTypeIds.StateIds.NotWaitingForConfirm;
        internal const uint Confirmation_WaitingForConfirm = ConfirmationStateMachineTypeIds.StateIds.WaitingForConfirm;

        /// <summary>
        /// ---- well-known transition identifiers (delegated to typed Ids classes) ----
        /// PrepareForUpdate
        /// </summary>
        internal const uint PrepareForUpdate_IdleToPreparing = PrepareForUpdateStateMachineTypeIds.TransitionIds.IdleToPreparing;
        internal const uint PrepareForUpdate_PreparingToIdle = PrepareForUpdateStateMachineTypeIds.TransitionIds.PreparingToIdle;
        internal const uint PrepareForUpdate_PreparingToPreparedForUpdate = PrepareForUpdateStateMachineTypeIds.TransitionIds.PreparingToPreparedForUpdate;
        internal const uint PrepareForUpdate_PreparedForUpdateToResuming = PrepareForUpdateStateMachineTypeIds.TransitionIds.PreparedForUpdateToResuming;

        /// <summary>
        /// Installation
        /// </summary>
        internal const uint Installation_IdleToInstalling = InstallationStateMachineTypeIds.TransitionIds.IdleToInstalling;
        internal const uint Installation_InstallingToIdle = InstallationStateMachineTypeIds.TransitionIds.InstallingToIdle;
        internal const uint Installation_InstallingToError = InstallationStateMachineTypeIds.TransitionIds.InstallingToError;
        internal const uint Installation_ErrorToIdle = InstallationStateMachineTypeIds.TransitionIds.ErrorToIdle;

        /// <summary>
        /// Confirmation
        /// </summary>
        internal const uint Confirmation_NotWaitingToWaiting = ConfirmationStateMachineTypeIds.TransitionIds.NotWaitingForConfirmToWaitingForConfirm;
        internal const uint Confirmation_WaitingToNotWaiting = ConfirmationStateMachineTypeIds.TransitionIds.WaitingForConfirmToNotWaitingForConfirm;

        /// <summary>
        /// ---- per-state browse-name + state-number lookup table ----
        /// (StateNumber values now sourced from the generator-emitted *Ids.StateNumbers classes
        /// so any future change in the DI model is picked up automatically.)
        /// </summary>
        private static readonly (uint Id, uint Number, string Name)[] s_states =
        [
            (PrepareForUpdate_Idle,
             PrepareForUpdateStateMachineTypeIds.StateNumbers.Idle, "Idle"),
            (PrepareForUpdate_Preparing,
             PrepareForUpdateStateMachineTypeIds.StateNumbers.Preparing, "Preparing"),
            (PrepareForUpdate_PreparedForUpdate,
             PrepareForUpdateStateMachineTypeIds.StateNumbers.PreparedForUpdate, "PreparedForUpdate"),
            (Installation_Idle,
             InstallationStateMachineTypeIds.StateNumbers.Idle, "Idle"),
            (Installation_Installing,
             InstallationStateMachineTypeIds.StateNumbers.Installing, "Installing"),
            (Installation_Error,
             InstallationStateMachineTypeIds.StateNumbers.Error, "Error"),
            (PowerCycle_NotWaiting,
             PowerCycleStateMachineTypeIds.StateNumbers.NotWaitingForPowerCycle, "NotWaitingForPowerCycle"),
            (PowerCycle_Waiting,
             PowerCycleStateMachineTypeIds.StateNumbers.WaitingForPowerCycle, "WaitingForPowerCycle"),
            (Confirmation_NotWaitingForConfirm,
             ConfirmationStateMachineTypeIds.StateNumbers.NotWaitingForConfirm, "NotWaitingForConfirm"),
            (Confirmation_WaitingForConfirm,
             ConfirmationStateMachineTypeIds.StateNumbers.WaitingForConfirm, "WaitingForConfirm"),
        ];

        /// <summary>
        /// ---- per-transition browse-name + transition-number lookup table ----
        /// </summary>
        private static readonly (uint Id, uint Number, string Name)[] s_transitions =
        [
            (PrepareForUpdate_IdleToPreparing,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.IdleToPreparing,
             "IdleToPreparing"),
            (PrepareForUpdate_PreparingToIdle,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.PreparingToIdle,
             "PreparingToIdle"),
            (PrepareForUpdate_PreparingToPreparedForUpdate,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.PreparingToPreparedForUpdate,
             "PreparingToPreparedForUpdate"),
            (PrepareForUpdate_PreparedForUpdateToResuming,
             PrepareForUpdateStateMachineTypeIds.TransitionNumbers.PreparedForUpdateToResuming,
             "PreparedForUpdateToResuming"),
            (Installation_IdleToInstalling,
             InstallationStateMachineTypeIds.TransitionNumbers.IdleToInstalling,
             "IdleToInstalling"),
            (Installation_InstallingToIdle,
             InstallationStateMachineTypeIds.TransitionNumbers.InstallingToIdle,
             "InstallingToIdle"),
            (Installation_InstallingToError,
             InstallationStateMachineTypeIds.TransitionNumbers.InstallingToError,
             "InstallingToError"),
            (Installation_ErrorToIdle,
             InstallationStateMachineTypeIds.TransitionNumbers.ErrorToIdle,
             "ErrorToIdle"),
            (Confirmation_NotWaitingToWaiting,
             ConfirmationStateMachineTypeIds.TransitionNumbers.NotWaitingForConfirmToWaitingForConfirm,
             "NotWaitingForConfirmToWaitingForConfirm"),
            (Confirmation_WaitingToNotWaiting,
             ConfirmationStateMachineTypeIds.TransitionNumbers.WaitingForConfirmToNotWaitingForConfirm,
             "WaitingForConfirmToNotWaitingForConfirm"),
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
            ApplyState(sm, initialStateId, diNamespaceIndex, context);
            ClearLastTransition(sm);
        }

        /// <summary>
        /// Writes <paramref name="stateId"/> into <paramref name="sm"/>'s
        /// <c>CurrentState</c> variable. Bypasses
        /// <see cref="FiniteStateMachineState"/>'s
        /// <c>UpdateStateVariable</c> (which requires a
        /// <c>StateTable</c> override the source-generated DI FSMs
        /// don't provide).
        /// </summary>
        internal static void ApplyState(
            FiniteStateMachineState sm,
            uint stateId,
            ushort diNamespaceIndex,
            ISystemContext context)
        {
            if (sm?.CurrentState is null)
            {
                return;
            }
            (uint _, uint number, string name) = Lookup(s_states, stateId, "Unknown");

            sm.CurrentState.Value = new LocalizedText(name);

            if (sm.CurrentState.Id is { } idVar)

            {

                idVar.Value = new NodeId(stateId, diNamespaceIndex);

            }
            if (sm.CurrentState.Number is { } numberVar)

            {

                numberVar.Value = number;

            }
            sm.CurrentState.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Writes <paramref name="transitionId"/> into <paramref name="sm"/>'s
        /// optional <c>LastTransition</c> variable (created lazily by
        /// <see cref="EnsureLastTransition(FiniteStateMachineState, ISystemContext)"/>).
        /// Sets <c>TransitionTime</c> to <see cref="DateTime.UtcNow"/>.
        /// </summary>
        internal static void ApplyTransition(
            FiniteStateMachineState sm,
            uint transitionId,
            ushort diNamespaceIndex,
            ISystemContext context)
        {
            if (sm is null)
            {
                return;
            }
            EnsureLastTransition(sm, context);

            if (sm.LastTransition is null)

            {

                return;

            }
            (uint _, uint number, string name) = Lookup(s_transitions, transitionId, "Unknown");

            sm.LastTransition.Value = new LocalizedText(name);

            if (sm.LastTransition.Id is { } idVar)

            {

                idVar.Value = new NodeId(transitionId, diNamespaceIndex);

            }
            if (sm.LastTransition.Number is { } numberVar)

            {

                numberVar.Value = number;

            }
            if (sm.LastTransition.TransitionTime is { } ttVar)

            {

                ttVar.Value = DateTime.UtcNow;

            }
            sm.LastTransition.ClearChangeMasks(context, includeChildren: true);
        }

        /// <summary>
        /// Atomically writes a state transition: sets
        /// <c>CurrentState</c> to <paramref name="toStateId"/> and
        /// <c>LastTransition</c> to <paramref name="transitionId"/>.
        /// </summary>
        internal static void Move(
            FiniteStateMachineState sm,
            uint toStateId,
            uint transitionId,
            ushort diNamespaceIndex,
            ISystemContext context)
        {
            ApplyState(sm, toStateId, diNamespaceIndex, context);
            ApplyTransition(sm, transitionId, diNamespaceIndex, context);
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
                logger?.LogWarning(
                    ex,
                    "Software-update state-change hook threw for phase {Phase}; swallowed.",
                    change.Phase);
            }
        }

        private static void ClearLastTransition(FiniteStateMachineState sm)
        {
            if (sm?.LastTransition is null)
            {
                return;
            }
            sm.LastTransition.Value = default;

            if (sm.LastTransition.Id is { } idVar)

            {

                idVar.Value = default;

            }
            if (sm.LastTransition.Number is { } numberVar)

            {

                numberVar.Value = 0;

            }
            if (sm.LastTransition.TransitionTime is { } ttVar)

            {

                ttVar.Value = DateTime.MinValue;

            }
        }

        private static void EnsureLastTransition(
            FiniteStateMachineState sm,
            ISystemContext context)
        {
            if (sm.LastTransition is not null)
            {
                return;
            }
            // The generated *StateMachineState classes inherit
            // AddLastTransition from FiniteStateMachineState; the
            // optional child wraps the standard Part-5 LastTransition
            // / FiniteTransitionVariableType (Value + Id + Number +
            // TransitionTime).
            sm.AddLastTransition(context);
        }

        private static (uint Id, uint Number, string Name) Lookup(
            (uint Id, uint Number, string Name)[] table,
            uint id,
            string fallbackName)
        {
            for (int ii = 0; ii < table.Length; ii++)
            {
                if (table[ii].Id == id)
                {
                    return table[ii];
                }
            }
            return (id, 0, fallbackName);
        }
    }
}
