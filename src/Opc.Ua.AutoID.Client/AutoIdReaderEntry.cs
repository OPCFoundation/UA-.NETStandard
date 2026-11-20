/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.AutoID.Client
{
    /// <summary>
    /// A reader device found by <see cref="AutoIdClient"/>.
    /// </summary>
    /// <param name="NodeId">The NodeId of the device.</param>
    /// <param name="BrowseName">The browse name of the device.</param>
    /// <param name="DisplayName">The display name of the device.</param>
    /// <param name="TypeDefinitionId">
    /// The type definition of the device; the concrete type or a vendor subtype of it.
    /// </param>
    /// <param name="Kind">The reader kind the type definition derives from.</param>
    public sealed record AutoIdReaderEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        string DisplayName,
        NodeId TypeDefinitionId,
        AutoIdReaderKind Kind);
}
