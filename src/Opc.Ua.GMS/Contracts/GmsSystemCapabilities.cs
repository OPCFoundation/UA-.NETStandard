// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.GMS.Contracts
{
    /// <summary>
    /// Describes actual configured native behavior, not certification or merely loaded namespaces.
    /// </summary>
    /// <param name="NodeId">The actual GMS root.</param>
    /// <param name="ConformanceUnits">Supported native CUs on this live configured instance.</param>
    /// <param name="ServerProfiles">
    /// Profiles whose required native, inherited and security dependencies are present.
    /// </param>
    /// <param name="Limitations">Precise unresolved upstream exceptions, without suppressing defined behavior.</param>
    public sealed record GmsSystemCapabilities(
        NodeId NodeId, ArrayOf<QualifiedName> ConformanceUnits, ArrayOf<string> ServerProfiles,
        ArrayOf<string> Limitations);
}
