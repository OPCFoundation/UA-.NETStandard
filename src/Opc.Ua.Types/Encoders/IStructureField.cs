// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// Field of a <see cref="IStructure"/>
    /// </summary>
    public interface IStructureField
    {
        /// <summary>
        /// Type information for the field
        /// </summary>
        TypeInfo TypeInfo { get; }

        /// <summary>
        /// Name of the field
        /// </summary>
        string? Name { get; }

        /// <summary>
        /// Whether the field is optional
        /// </summary>
        bool IsOptional { get; }
    }
}
