/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Type is nullable in the OPC UA type system. This is used
    /// to support null values for value types and to represent
    /// the logical absence of a value.
    /// </summary>
    public interface INullable
    {
        /// <summary>
        /// Is null
        /// </summary>
        bool IsNull { get; }
    }
}
