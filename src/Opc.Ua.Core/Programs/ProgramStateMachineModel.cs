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
