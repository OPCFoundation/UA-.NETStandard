/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Interface implemented by structured type instances.
    /// </summary>
    public interface IStructureTypeInfo
    {
        /// <summary>
        /// Gets the structure type.
        /// </summary>
        StructureType StructureType { get; }
    }
}
