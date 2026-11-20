/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// What part of the stack to generate
    /// </summary>
    [Flags]
    public enum StackGenerationType
    {
        /// <summary>
        /// Generate nothing
        /// </summary>
        None,

        /// <summary>
        /// Generate the .NET stack code
        /// </summary>
        Stack,

        /// <summary>
        /// Generate the core models
        /// </summary>
        Models,

        /// <summary>
        /// Generate both stack and models
        /// </summary>
        All
    }
}
