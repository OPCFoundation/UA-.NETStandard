/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Vision.Client
{
    /// <summary>
    /// A discovered instance under the Vision root.
    /// </summary>
    /// <param name="NodeId">
    /// The instance NodeId.
    /// </param>
    /// <param name="BrowseName">
    /// The instance BrowseName as reported by the Server.
    /// </param>
    /// <param name="DisplayName">
    /// The instance DisplayName as reported by the Server.
    /// </param>
    /// <param name="TypeDefinitionId">
    /// The resolved type definition NodeId.
    /// </param>
    public sealed record VisionNodeEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinitionId);
}
