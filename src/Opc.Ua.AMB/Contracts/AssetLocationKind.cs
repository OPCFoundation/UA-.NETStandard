// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.AMB
{
    /// <summary>
    /// The kinds of location OPC 10000-110 §13 distinguishes.
    /// </summary>
    public enum AssetLocationKind
    {
        /// <summary>
        /// Where the asset is in a hierarchy such as plant, hall and line
        /// (§13.3): the <c>HierarchicalLocation</c> Property and the
        /// <c>HierarchicalLocations</c> objects.
        /// </summary>
        Hierarchical,

        /// <summary>
        /// Where the asset is used operationally (§13.4): the
        /// <c>OperationalLocation</c> Property and the
        /// <c>OperationalLocations</c> objects.
        /// </summary>
        Operational,

        /// <summary>
        /// Where a digital asset is deployed (§13.5): the
        /// <c>DigitalLocation</c> Property. It has no location objects.
        /// </summary>
        Digital
    }
}
