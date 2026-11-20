// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Defines a contract for emitting generated code.
    /// </summary>
    internal interface IGenerator
    {
        /// <summary>
        /// Emit the generated code.
        /// </summary>
        IEnumerable<Resource> Emit();
    }
}
