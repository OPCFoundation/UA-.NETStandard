/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.Collections.Generic;
using Opc.Ua.Server.UserDatabase;

namespace Opc.Ua.Gds.Server
{
    /// <summary>
    /// Extends <see cref="IUserDatabase"/> with GDS-specific user
    /// properties required for the <see cref="GdsRole.ApplicationAdmin"/>
    /// privilege (OPC 10000-12 §7.2).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The base <see cref="IUserDatabase"/> stores user credentials and
    /// roles but has no concept of per-user application bindings. A GDS
    /// that supports the <c>ApplicationAdmin</c> privilege needs to
    /// know which <c>ApplicationId</c>s each user may administer.
    /// </para>
    /// <para>
    /// Implement this interface (instead of plain
    /// <see cref="IUserDatabase"/>) and pass it to
    /// <see cref="GlobalDiscoverySampleServer"/> so that the
    /// impersonation handler can populate
    /// <see cref="GdsRoleBasedIdentity.AdministeredApplicationIds"/>
    /// automatically.
    /// </para>
    /// <example>
    /// <code>
    /// public class MyGdsUserDatabase : LinqUserDatabase, IGdsUserDatabase
    /// {
    ///     public IReadOnlyList&lt;NodeId&gt; GetAdministeredApplicationIds(string userName)
    ///     {
    ///         // look up the mapping from your persistent store
    ///         return _appAdminMap.TryGetValue(userName, out var ids) ? ids : null;
    ///     }
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    public interface IGdsUserDatabase : IUserDatabase
    {
        /// <summary>
        /// Returns the set of <c>ApplicationId</c>s the specified user
        /// is allowed to administer, or <c>null</c> / empty when the
        /// user does not hold the <see cref="GdsRole.ApplicationAdmin"/>
        /// privilege.
        /// </summary>
        /// <param name="userName">The user name.</param>
        /// <returns>
        /// A list of <see cref="NodeId"/>s representing the registered
        /// applications the user may administer, or <c>null</c>.
        /// </returns>
        IReadOnlyList<NodeId>? GetAdministeredApplicationIds(string userName);
    }
}
