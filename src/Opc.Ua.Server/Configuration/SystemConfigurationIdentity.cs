/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

#if !NET9_0_OR_GREATER
#endif

namespace Opc.Ua.Server
{
    /// <summary>
    /// Privileged identity which can access the system configuration.
    /// </summary>
    public class SystemConfigurationIdentity : RoleBasedIdentity
    {
        /// <summary>
        /// Create a user identity with the privilege
        /// to modify the system configuration.
        /// </summary>
        /// <param name="identity">The user identity.</param>
        public SystemConfigurationIdentity(IUserIdentity identity)
            : base(identity, [Role.SecurityAdmin, Role.ConfigureAdmin, Role.AuthenticatedUser], new NamespaceTable())
        {
        }
    }
}
