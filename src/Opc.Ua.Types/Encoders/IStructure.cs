// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Interface to access properties of a complex type.
    /// </summary>
    public interface IStructure
    {
        /// <summary>
        /// Fields of the structure
        /// </summary>
        IReadOnlyList<IStructureField> GetFields();

        /// <summary>
        /// Access property values by index.
        /// </summary>
        Variant this[int index] { get; set; }

        /// <summary>
        /// Access property values by name.
        /// </summary>
        Variant this[string name] { get; set; }
    }
}
