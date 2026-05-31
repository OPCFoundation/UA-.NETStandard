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
        // ---- well-known state identifiers (numeric ids in the DI ns) ----
        // PrepareForUpdate
        internal const uint PrepareForUpdate_Idle = Opc.Ua.Di.Objects.PrepareForUpdateStateMachineType_Idle;
        internal const uint PrepareForUpdate_Preparing = Opc.Ua.Di.Objects.PrepareForUpdateStateMachineType_Preparing;
        internal const uint PrepareForUpdate_PreparedForUpdate = Opc.Ua.Di.Objects.PrepareForUpdateStateMachineType_PreparedForUpdate;

        // Installation
        internal const uint Installation_Idle = Opc.Ua.Di.Objects.InstallationStateMachineType_Idle;
        internal const uint Installation_Installing = Opc.Ua.Di.Objects.InstallationStateMachineType_Installing;
        internal const uint Installation_Error = Opc.Ua.Di.Objects.InstallationStateMachineType_Error;

        // PowerCycle
        internal const uint PowerCycle_NotWaiting = Opc.Ua.Di.Objects.PowerCycleStateMachineType_NotWaitingForPowerCycle;
        internal const uint PowerCycle_Waiting = Opc.Ua.Di.Objects.PowerCycleStateMachineType_WaitingForPowerCycle;

        // Confirmation
        internal const uint Confirmation_NotWaitingForConfirm = Opc.Ua.Di.Objects.ConfirmationStateMachineType_NotWaitingForConfirm;
        internal const uint Confirmation_WaitingForConfirm = Opc.Ua.Di.Objects.ConfirmationStateMachineType_WaitingForConfirm;

        // ---- well-known transition identifiers ----
        // PrepareForUpdate
        internal const uint PrepareForUpdate_IdleToPreparing = Opc.Ua.Di.Objects.PrepareForUpdateStateMachineType_IdleToPreparing;
        internal const uint PrepareForUpdate_PreparingToIdle = Opc.Ua.Di.Objects.PrepareForUpdateStateMachineType_PreparingToIdle;
        internal const uint PrepareForUpdate_PreparingToPreparedForUpdate = Opc.Ua.Di.Objects.PrepareForUpdateStateMachineType_PreparingToPreparedForUpdate;
        internal const uint PrepareForUpdate_PreparedForUpdateToResuming = Opc.Ua.Di.Objects.PrepareForUpdateStateMachineType_PreparedForUpdateToResuming;

        // Installation
        internal const uint Installation_IdleToInstalling = Opc.Ua.Di.Objects.InstallationStateMachineType_IdleToInstalling;
        internal const uint Installation_InstallingToIdle = Opc.Ua.Di.Objects.InstallationStateMachineType_InstallingToIdle;
        internal const uint Installation_InstallingToError = Opc.Ua.Di.Objects.InstallationStateMachineType_InstallingToError;
        internal const uint Installation_ErrorToIdle = Opc.Ua.Di.Objects.InstallationStateMachineType_ErrorToIdle;

        // Confirmation
        internal const uint Confirmation_NotWaitingToWaiting = Opc.Ua.Di.Objects.ConfirmationStateMachineType_NotWaitingForConfirmToWaitingForConfirm;
        internal const uint Confirmation_WaitingToNotWaiting = Opc.Ua.Di.Objects.ConfirmationStateMachineType_WaitingForConfirmToNotWaitingForConfirm;

        // ---- per-state browse-name + state-number lookup table ----
        // Layout: (stateId, stateNumber, displayName)
        private static readonly (uint Id, uint Number, string Name)[] s_states =
        [
            (PrepareForUpdate_Idle,                1, "Idle"),
            (PrepareForUpdate_Preparing,           2, "Preparing"),
            (PrepareForUpdate_PreparedForUpdate,   3, "PreparedForUpdate"),
            (Installation_Idle,                    1, "Idle"),
            (Installation_Installing,              2, "Installing"),
            (Installation_Error,                   3, "Error"),
            (PowerCycle_NotWaiting,                1, "NotWaitingForPowerCycle"),
            (PowerCycle_Waiting,                   2, "WaitingForPowerCycle"),
            (Confirmation_NotWaitingForConfirm,    1, "NotWaitingForConfirm"),
            (Confirmation_WaitingForConfirm,       2, "WaitingForConfirm"),
        ];

        // ---- per-transition browse-name + transition-number lookup table ----
        private static readonly (uint Id, uint Number, string Name)[] s_transitions =
        [
            (PrepareForUpdate_IdleToPreparing,                  12, "IdleToPreparing"),
            (PrepareForUpdate_PreparingToIdle,                  21, "PreparingToIdle"),
            (PrepareForUpdate_PreparingToPreparedForUpdate,     23, "PreparingToPreparedForUpdate"),
            (PrepareForUpdate_PreparedForUpdateToResuming,      34, "PreparedForUpdateToResuming"),
            (Installation_IdleToInstalling,                     12, "IdleToInstalling"),
            (Installation_InstallingToIdle,                     21, "InstallingToIdle"),
            (Installation_InstallingToError,                    23, "InstallingToError"),
            (Installation_ErrorToIdle,                          31, "ErrorToIdle"),
            (Confirmation_NotWaitingToWaiting,                  12, "NotWaitingForConfirmToWaitingForConfirm"),
            (Confirmation_WaitingToNotWaiting,                  21, "WaitingForConfirmToNotWaitingForConfirm"),
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
