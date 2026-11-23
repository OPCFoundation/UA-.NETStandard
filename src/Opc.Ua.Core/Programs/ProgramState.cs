// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Programs
{
    /// <summary>
    /// The four base Program states, with the StateNumber values defined by OPC UA Part 10.
    /// </summary>
    public enum ProgramState
    {
        /// <summary>
        /// The invocation is halted and must be reset before it can start.
        /// </summary>
        Halted = 11,

        /// <summary>
        /// The invocation is initialized and can start.
        /// </summary>
        Ready = 12,

        /// <summary>
        /// The invocation is executing.
        /// </summary>
        Running = 13,

        /// <summary>
        /// The invocation is suspended and can resume the current run.
        /// </summary>
        Suspended = 14
    }
}
