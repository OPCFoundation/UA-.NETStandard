// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.PADIM.Client
{
    /// <summary>
    /// A discovered device, analyser, signal or modular subdevice.
    /// </summary>
    public sealed record PadimEntry(
        NodeId NodeId,
        QualifiedName BrowseName,
        LocalizedText DisplayName,
        NodeId TypeDefinitionId);

    /// <summary>
    /// An acquired value with its node identity and unmodified quality and timestamps.
    /// </summary>
    public sealed record PadimValue(NodeId NodeId, QualifiedName BrowseName, DataValue Sample);
}
