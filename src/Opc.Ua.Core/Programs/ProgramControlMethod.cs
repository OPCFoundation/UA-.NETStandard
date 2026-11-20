// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Programs
{
    /// <summary>
    /// The optional Program control methods defined by OPC UA Part 10.
    /// </summary>
    public enum ProgramControlMethod
    {
        /// <summary>
        /// Starts a ready invocation.
        /// </summary>
        Start,

        /// <summary>
        /// Suspends a running invocation.
        /// </summary>
        Suspend,

        /// <summary>
        /// Resumes a suspended invocation.
        /// </summary>
        Resume,

        /// <summary>
        /// Halts a ready, running or suspended invocation.
        /// </summary>
        Halt,

        /// <summary>
        /// Resets a halted invocation.
        /// </summary>
        Reset
    }
}
