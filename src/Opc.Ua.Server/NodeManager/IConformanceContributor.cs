/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.Server
{
    /// <summary>
    /// Implemented by a node manager (or other server feature) that contributes
    /// the OPC UA conformance units and server profiles it enables.
    /// </summary>
    /// <remarks>
    /// A server can aggregate these contributions with its always-supported base
    /// set to publish <c>Server/ServerCapabilities/ConformanceUnits</c> and the
    /// <c>ServerProfileArray</c> derived from what is actually wired up (which
    /// node managers were added / which features were enabled via DI) rather than
    /// a fixed hard-coded list. See OPC UA Part 7 (Profiles) and Part 5
    /// (ServerCapabilities). Contributors that enable nothing return empty
    /// collections; the aggregating server de-duplicates across contributors.
    /// </remarks>
    public interface IConformanceContributor
    {
        /// <summary>
        /// The conformance units enabled by this contributor (may be empty).
        /// </summary>
        ArrayOf<QualifiedName> ConformanceUnits { get; }

        /// <summary>
        /// The server profile URIs enabled by this contributor (may be empty).
        /// </summary>
        ArrayOf<string> ServerProfiles { get; }
    }
}
