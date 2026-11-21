// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua.Client.Roles
{
    /// <summary>
    /// Convenience extensions for opening a <see cref="RoleManagementClient"/>
    /// against an <see cref="ISession"/>.
    /// </summary>
    public static class SessionRoleExtensions
    {
        /// <summary>
        /// Returns a <see cref="RoleManagementClient"/> rooted at the
        /// server's standard <c>Server.ServerCapabilities.RoleSet</c> object.
        /// </summary>
        public static RoleManagementClient OpenRoleManagementClient(this ISession session)
        {
            return new RoleManagementClient(session);
        }
    }
}
