/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Programs
{
    /// <summary>
    /// Maps Program states, transitions and control methods to namespace-zero model identifiers.
    /// </summary>
    public static class ProgramStateMachineModel
    {
        /// <summary>
        /// Gets the standard state node identifier, not its StateNumber.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The state is undefined.</exception>
        public static NodeId GetStateId(ProgramState state)
        {
            return new NodeId(state switch
            {
                ProgramState.Halted => global::Opc.Ua.Objects.ProgramStateMachineType_Halted,
                ProgramState.Ready => global::Opc.Ua.Objects.ProgramStateMachineType_Ready,
                ProgramState.Running => global::Opc.Ua.Objects.ProgramStateMachineType_Running,
                ProgramState.Suspended => global::Opc.Ua.Objects.ProgramStateMachineType_Suspended,
                _ => throw new ArgumentOutOfRangeException(nameof(state))
            });
        }

        /// <summary>
        /// Gets the standard control method declaration identifier.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">The method is undefined.</exception>
        public static NodeId GetMethodId(ProgramControlMethod method)
        {
            return new NodeId(method switch
            {
                ProgramControlMethod.Start => global::Opc.Ua.Methods.ProgramStateMachineType_Start,
                ProgramControlMethod.Suspend => global::Opc.Ua.Methods.ProgramStateMachineType_Suspend,
                ProgramControlMethod.Resume => global::Opc.Ua.Methods.ProgramStateMachineType_Resume,
                ProgramControlMethod.Halt => global::Opc.Ua.Methods.ProgramStateMachineType_Halt,
                ProgramControlMethod.Reset => global::Opc.Ua.Methods.ProgramStateMachineType_Reset,
                _ => throw new ArgumentOutOfRangeException(nameof(method))
            });
        }

        /// <summary>
        /// Gets one of the nine standard Program transitions, including internal transitions.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">Either state is undefined.</exception>
        /// <exception cref="ArgumentException">The states do not define a standard transition.</exception>
        public static NodeId GetTransitionId(ProgramState from, ProgramState to)
        {
            if (from is < ProgramState.Halted or > ProgramState.Suspended)
            {
                throw new ArgumentOutOfRangeException(nameof(from));
            }
            if (to is < ProgramState.Halted or > ProgramState.Suspended)
            {
                throw new ArgumentOutOfRangeException(nameof(to));
            }
            return new NodeId((from, to) switch
            {
                (ProgramState.Halted, ProgramState.Ready) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_HaltedToReady,
                (ProgramState.Ready, ProgramState.Running) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_ReadyToRunning,
                (ProgramState.Running, ProgramState.Halted) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_RunningToHalted,
                (ProgramState.Running, ProgramState.Ready) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_RunningToReady,
                (ProgramState.Running, ProgramState.Suspended) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_RunningToSuspended,
                (ProgramState.Suspended, ProgramState.Running) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_SuspendedToRunning,
                (ProgramState.Suspended, ProgramState.Halted) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_SuspendedToHalted,
                (ProgramState.Suspended, ProgramState.Ready) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_SuspendedToReady,
                (ProgramState.Ready, ProgramState.Halted) =>
                    global::Opc.Ua.Objects.ProgramStateMachineType_ReadyToHalted,
                _ => throw new ArgumentException("The states do not define a standard Program transition.", nameof(to))
            });
        }

        /// <summary>
        /// Tries to map a namespace-zero state node identifier to a base Program state.
        /// </summary>
        public static bool TryGetState(NodeId stateId, out ProgramState state)
        {
            state = default;
            if (stateId.NamespaceIndex != 0 || !stateId.TryGetValue(out uint id))
            {
                return false;
            }

            switch (id)
            {
                case global::Opc.Ua.Objects.ProgramStateMachineType_Halted:
                    state = ProgramState.Halted;
                    return true;
                case global::Opc.Ua.Objects.ProgramStateMachineType_Ready:
                    state = ProgramState.Ready;
                    return true;
                case global::Opc.Ua.Objects.ProgramStateMachineType_Running:
                    state = ProgramState.Running;
                    return true;
                case global::Opc.Ua.Objects.ProgramStateMachineType_Suspended:
                    state = ProgramState.Suspended;
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Tries to map a namespace-zero control method declaration identifier to a Program control method.
        /// </summary>
        public static bool TryGetControlMethod(NodeId methodId, out ProgramControlMethod method)
        {
            method = default;
            if (methodId.NamespaceIndex != 0 || !methodId.TryGetValue(out uint id))
            {
                return false;
            }

            switch (id)
            {
                case global::Opc.Ua.Methods.ProgramStateMachineType_Start:
                    method = ProgramControlMethod.Start;
                    return true;
                case global::Opc.Ua.Methods.ProgramStateMachineType_Suspend:
                    method = ProgramControlMethod.Suspend;
                    return true;
                case global::Opc.Ua.Methods.ProgramStateMachineType_Resume:
                    method = ProgramControlMethod.Resume;
                    return true;
                case global::Opc.Ua.Methods.ProgramStateMachineType_Halt:
                    method = ProgramControlMethod.Halt;
                    return true;
                case global::Opc.Ua.Methods.ProgramStateMachineType_Reset:
                    method = ProgramControlMethod.Reset;
                    return true;
                default:
                    return false;
            }
        }
    }
}
