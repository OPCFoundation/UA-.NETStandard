// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Robotics.Client
{
    /// <summary>
    /// A discovered MotionDeviceSystem instance.
    /// </summary>
    /// <param name="NodeId">
    /// The system NodeId.
    /// </param>
    /// <param name="BrowseName">
    /// The system BrowseName.
    /// </param>
    /// <param name="DisplayName">
    /// The system DisplayName.
    /// </param>
    /// <param name="TypeDefinitionId">
    /// The resolved type definition NodeId.
    /// </param>
    public sealed record MotionDeviceSystemEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinitionId);
}
