// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Di.Server.Builders
{
    /// <summary>
    /// Classification of a <see cref="LifetimeVariableState"/> per
    /// OPC 10000-100 §10.6. Determines which
    /// <c>BaseLifetimeIndicationType</c> subtype is referenced from the
    /// variable's <c>Indication</c> property.
    /// </summary>
    public enum LifetimeIndicationKind
    {
        /// <summary>
        /// Time-based lifetime (e.g. operating hours).
        /// </summary>
        Time,

        /// <summary>
        /// Count of produced parts.
        /// </summary>
        NumberOfParts,

        /// <summary>
        /// Count of usage cycles.
        /// </summary>
        NumberOfUsages,

        /// <summary>
        /// Linear length consumed (m).
        /// </summary>
        Length,

        /// <summary>
        /// Diameter remaining (m).
        /// </summary>
        Diameter,

        /// <summary>
        /// Substance volume remaining (m³).
        /// </summary>
        SubstanceVolume
    }
}
