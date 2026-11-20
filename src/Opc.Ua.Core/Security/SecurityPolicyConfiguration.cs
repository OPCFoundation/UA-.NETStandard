/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Carries a pending security policy registration through the container.
    /// </summary>
    public sealed class SecurityPolicyConfiguration
    {
        /// <summary>
        /// Initializes the pending registration.
        /// </summary>
        /// <param name="securityPolicy">The security policy to register.</param>
        /// <param name="replaceExisting">Whether to deliberately replace an existing policy with the same name or URI.</param>
        public SecurityPolicyConfiguration(SecurityPolicyInfo securityPolicy, bool replaceExisting)
        {
            SecurityPolicy = securityPolicy ?? throw new ArgumentNullException(nameof(securityPolicy));
            ReplaceExisting = replaceExisting;
        }

        /// <summary>
        /// The security policy to register.
        /// </summary>
        public SecurityPolicyInfo SecurityPolicy { get; }

        /// <summary>
        /// Whether an existing policy with the same name or URI is deliberately replaced.
        /// </summary>
        public bool ReplaceExisting { get; }

        /// <summary>
        /// Applies the registration to a registry.
        /// </summary>
        /// <param name="registry">The registry to configure.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Apply(ISecurityPolicyRegistry registry)
        {
            if (registry is null)
            {
                throw new ArgumentNullException(nameof(registry));
            }

            registry.Register(SecurityPolicy, ReplaceExisting);
        }
    }
}
