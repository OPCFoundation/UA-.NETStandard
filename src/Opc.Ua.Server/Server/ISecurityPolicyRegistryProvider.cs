// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua.Server
{
    /// <summary>
    /// Optional opt-in interface that exposes a server's
    /// <see cref="ISecurityPolicyRegistry"/> so subordinate components can
    /// resolve policy URIs against the set the application composed, without
    /// any change to <see cref="IServerInternal"/>; external/mocked
    /// <see cref="IServerInternal"/> implementations remain unaffected and can
    /// fall back to <see cref="SecurityPolicies.Default"/> when this interface
    /// is not implemented.
    /// </summary>
    /// <remarks>
    /// Consumers should resolve the registry in a null-safe manner, e.g.:
    /// <code>
    /// var policies = (server as ISecurityPolicyRegistryProvider)?.SecurityPolicyRegistry
    ///                ?? SecurityPolicies.Default;
    /// </code>
    /// </remarks>
    public interface ISecurityPolicyRegistryProvider
    {
        /// <summary>
        /// Gets the server's <see cref="ISecurityPolicyRegistry"/>; never
        /// <c>null</c>. Defaults to <see cref="SecurityPolicies.Default"/>
        /// when the server was constructed without an explicit registry.
        /// </summary>
        ISecurityPolicyRegistry SecurityPolicyRegistry { get; }
    }
}
