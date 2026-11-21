// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua.Server.AliasNames
{
    /// <summary>
    /// The optional Part 17 children that an <see cref="AliasNameCategoryDescriptor"/>
    /// exposes on its <c>AliasNameCategoryType</c> instance. The mandatory
    /// <c>FindAlias</c> method is always created; this enum controls only the
    /// optional members defined by Part 17 §6.3.1.
    /// </summary>
    [Flags]
    public enum AliasNameCapabilities
    {
        /// <summary>
        /// Only the mandatory <c>FindAlias</c> method is exposed.
        /// </summary>
        None = 0,

        /// <summary>
        /// The category exposes <c>FindAliasVerbose</c> (Part 17 §6.3.3).
        /// </summary>
        FindAliasVerbose = 1 << 0,

        /// <summary>
        /// The category exposes <c>AddAliasesToCategory</c> (Part 17 §6.3.4).
        /// </summary>
        AddAliasesToCategory = 1 << 1,

        /// <summary>
        /// The category exposes <c>DeleteAliasesFromCategory</c> (Part 17 §6.3.5).
        /// </summary>
        DeleteAliasesFromCategory = 1 << 2,

        /// <summary>
        /// The category exposes the <c>LastChange</c> property and reflects
        /// updates to it (Part 17 §6.3.1).
        /// </summary>
        LastChange = 1 << 3,

        /// <summary>
        /// Convenience value: read-only category supporting both find variants
        /// plus <c>LastChange</c>.
        /// </summary>
        ReadOnly = FindAliasVerbose | LastChange,

        /// <summary>
        /// Convenience value: read-write category supporting every Part 17
        /// optional member.
        /// </summary>
        All = FindAliasVerbose | AddAliasesToCategory | DeleteAliasesFromCategory | LastChange
    }
}
