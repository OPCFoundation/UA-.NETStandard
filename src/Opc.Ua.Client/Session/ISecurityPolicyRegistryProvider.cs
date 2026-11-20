// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Client
{
    /// <summary>
    /// Optionally exposes the security policy registry used by a session factory.
    /// </summary>
    public interface ISecurityPolicyRegistryProvider
    {
        /// <summary>
        /// Gets the security policy registry, or <see langword="null"/> to use
        /// <see cref="SecurityPolicies.Default"/>.
        /// </summary>
        ISecurityPolicyRegistry? SecurityPolicyRegistry { get; }
    }
}
